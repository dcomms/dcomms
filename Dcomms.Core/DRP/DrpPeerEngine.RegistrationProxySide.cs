using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {
        /// <summary>
        /// main register responder proc for both A-EP and P2P modes
        /// in P2P mode SenderToken32 and SenderHMAC are verified at this time
        /// </summary>
        /// <param name="receivedFromInP2pMode">
        /// is null in A-EP mode
        /// </param>
        internal async Task ProxyRegisterRequestAsync(ConnectionToNeighbor responder, 
            RegisterRequestPacket syn, IPEndPoint requesterEndpoint, 
            ConnectionToNeighbor requester) // engine thread
        {
            WriteToLog_reg_proxySide_detail($"proxying registration: remoteEndpoint={requesterEndpoint}, NpaSeq16={syn.NpaSeq16}, responder={responder}, synReceivedFromInP2pMode={requester}");
            
            if (syn.AtoEP ^ (requester == null))
                throw new InvalidOperationException();
            _recentUniqueRegistrationRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);

            if (!ValidateReceivedSynTimestamp32S(syn.Timestamp32S))
                throw new BadSignatureException();

            if (syn.NumberOfHopsRemaining <= 1)
            {
                SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero, requester);
                return;
            }

            _pendingRegisterRequests.Add(syn.RequesterPublicKey_RequestID);
            try
            {

                // send NPACK to requester
                WriteToLog_reg_proxySide_detail($"sending NPACK to REQ requester");
                SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.accepted, requester);

                syn.NumberOfHopsRemaining--;

                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ   
                syn.NpaSeq16 = responder.GetNewNhaSeq16_P2P();
                await responder.SendUdpRequestAsync_Retransmit_WaitForNPACK(syn.Encode_OptionallySignSenderHMAC(responder),
                    syn.NpaSeq16, syn.GetSignedFieldsForSenderHMAC);

                // wait for SYNACK from responder
                // verify SenderHMAC
                WriteToLog_reg_proxySide_detail($"waiting for SYNACK from responder");
                var registerSynAckPacketData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(responder.RemoteEndpoint,
                                RegisterAck1Packet.GetScanner(syn.RequesterPublicKey_RequestID, syn.Timestamp32S, responder),
                                    DateTimeNowUtc, Configuration.RegSynAckTimoutS
                                ));
                if (registerSynAckPacketData == null) throw new DrpTimeoutException("Did not receive SYNACK on timeout");
                var synAck = RegisterAck1Packet.DecodeAndOptionallyVerify(registerSynAckPacketData, null, null);
                WriteToLog_reg_proxySide_detail($"verified SYNACK from responder");

                // respond with NPACK
                SendNextHopAckResponseToSynAck(synAck, responder);

                // send SYNACK to requester 
                // wait for ACK / NPACK 
                if (requester != null)
                {   // P2P mode
                    synAck.NpaSeq16 = requester.GetNewNhaSeq16_P2P();
                }
                else
                {   // A-EP mode
                    synAck.RequesterEndpoint = requesterEndpoint;
                }
                var synAckUdpData = synAck.Encode_OpionallySignSenderHMAC(requester);


                var ackScanner = RegisterAck2Packet.GetScanner(requester, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                byte[] ackUdpData;
                if (requester == null)
                {   // A-EP mode: wait for ACK, retransmitting SYNACK
                    WriteToLog_reg_proxySide_detail($"sending SYNACK, waiting for ACK");
                    ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(synAckUdpData, requesterEndpoint, ackScanner);
                }
                else
                {   // P2P mode: retransmit SYNACK until NPACK (via P2P); at same time wait for ACK
                    WriteToLog_reg_proxySide_detail($"sending SYNACK, awaiting for NPACK");
                    _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(synAckUdpData, requesterEndpoint,
                        synAck.NpaSeq16, requester, synAck.GetSignedFieldsForSenderHMAC);
                    // not waiting for NPACK, wait for ACK
                    WriteToLog_reg_proxySide_detail($"waiting for ACK");
                    ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, ackScanner);
                }

                WriteToLog_reg_proxySide_detail($"received ACK");
                var ack = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(ackUdpData, null, null, null);
                
                // send NPACK to requester
                WriteToLog_reg_proxySide_detail($"sending NPACK to ACK to requester");
                SendNextHopAckResponseToAck(ack, requesterEndpoint, requester);
                
                // send ACK to responder
                // put ACK.NpaSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                ack.NpaSeq16 = responder.GetNewNhaSeq16_P2P();
                await responder.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack.Encode_OptionallySignSenderHMAC(responder),
                    ack.NpaSeq16, ack.GetSignedFieldsForSenderHMAC);


                // wait for CFM from requester
                var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null,
                    requesterEndpoint,
                    RegisterConfirmationPacket.GetScanner(requester, syn.RequesterPublicKey_RequestID, syn.Timestamp32S)
                    );
                var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);

                // TODO verify signatures and update quality/rating

                // send NPACK to requester
                SendNextHopAckResponseToCfm(cfm, requesterEndpoint, requester);

                // send CFM to responder
                // wait for NPACK from responder, retransmit
                cfm.NpaSeq16 = responder.GetNewNhaSeq16_P2P();
                await responder.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfm.Encode_OptionallySignSenderHMAC(responder),
                    cfm.NpaSeq16, cfm.GetSignedFieldsForSenderHMAC);
            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxyingRegister(requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(syn.RequesterPublicKey_RequestID);
            }
        }
    }
}
