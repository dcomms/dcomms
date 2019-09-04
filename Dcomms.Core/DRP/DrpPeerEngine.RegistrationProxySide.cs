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
        internal async Task ProxyRegisterRequestAsync(ConnectionToNeighbor destinationPeer, 
            RegisterSynPacket syn, IPEndPoint requesterEndpoint, 
            ConnectionToNeighbor synReceivedFromInP2pMode) // engine thread
        {
            WriteToLog_reg_proxySide_detail($"proxying registration: remoteEndpoint={requesterEndpoint}, NhaSeq16={syn.NhaSeq16}, destinationPeer={destinationPeer}, synReceivedFromInP2pMode={synReceivedFromInP2pMode}");
            
            if (syn.AtoEP ^ (synReceivedFromInP2pMode == null))
                throw new InvalidOperationException();

            _pendingRegisterRequests.Add(syn.RequesterPublicKey_RequestID);
            try
            {
                if (!ValidateReceivedSynTimestamp32S(syn.Timestamp32S))
                    throw new BadSignatureException();

                syn.NumberOfHopsRemaining--;
                if (syn.NumberOfHopsRemaining == 0)
                {
                    SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero, synReceivedFromInP2pMode);
                    return;
                }

                // send NHACK to requester
                WriteToLog_reg_proxySide_detail($"sending NHACK to SYN requester");
                SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.accepted, synReceivedFromInP2pMode);

                // send (proxy) SYN to destinationPeer. wait for NHACK, verify NHACK.senderHMAC, retransmit SYN   
                syn.NhaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNHACK(syn.Encode_OptionallySignSenderHMAC(destinationPeer),
                    syn.NhaSeq16, destinationPeer, syn.GetSignedFieldsForSenderHMAC);

                // wait for SYNACK from destinationPeer
                // verify SenderHMAC
                WriteToLog_reg_proxySide_detail($"waiting for SYNACK from destinationPeer");
                var registerSynAckPacketData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                RegisterSynAckPacket.GetScanner(syn.RequesterPublicKey_RequestID, syn.Timestamp32S, destinationPeer),
                                    DateTimeNowUtc, Configuration.RegSynAckRequesterSideTimoutS
                                ));
                if (registerSynAckPacketData == null) throw new DrpTimeoutException("Did not receive SYNACK on timeout");
                var synAck = RegisterSynAckPacket.DecodeAndOptionallyVerify(registerSynAckPacketData, null, null);

                WriteToLog_reg_proxySide_detail($"verified SYNACK from destinationPeer");

                // respond with NHACK
                SendNextHopAckResponseToSynAck(synAck, destinationPeer);

                // send SYNACK to requester 
                // wait for ACK / NHACK 
                if (synReceivedFromInP2pMode != null)
                {   // P2P mode
                    synAck.NhaSeq16 = synReceivedFromInP2pMode.GetNewNhaSeq16_P2P();
                }
                else
                {   // A-EP mode
                    synAck.RequesterEndpoint = requesterEndpoint;
                }
                var synAckUdpData = synAck.EncodeAtResponder(synReceivedFromInP2pMode);


                var ackScanner = RegisterAckPacket.GetScanner(synReceivedFromInP2pMode, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                byte[] ackUdpData;
                if (synReceivedFromInP2pMode == null)
                {   // A-EP mode: wait for ACK, retransmitting SYNACK
                    WriteToLog_reg_proxySide_detail($"sending SYNACK, waiting for ACK");
                    ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(synAckUdpData, requesterEndpoint, ackScanner);
                }
                else
                {   // P2P mode: retransmit SYNACK until NHACK (via P2P); at same time wait for ACK
                    WriteToLog_reg_proxySide_detail($"sending SYNACK, awaiting for NHACK");
                    _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNextHopAck(synAckUdpData, requesterEndpoint,
                        synAck.NhaSeq16, synReceivedFromInP2pMode, synAck.GetSignedFieldsForSenderHMAC);
                    // not waiting for NHACK, wait for ACK
                    WriteToLog_reg_proxySide_detail($"waiting for ACK");
                    ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, ackScanner);
                }

                WriteToLog_reg_proxySide_detail($"received ACK");
                var ack = RegisterAckPacket.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(ackUdpData, null, null, null);
                
                // send NHACK to requester
                WriteToLog_reg_proxySide_detail($"sending NHACK to ACK to requester");
                SendNextHopAckResponseToAck(ack, requesterEndpoint, synReceivedFromInP2pMode);
                
                // send ACK to destinationPeer
                // put ACK.NhaSeq16, sendertoken32, senderHMAC  
                // wait for NHACK
                ack.NhaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNHACK(ack.Encode_OptionallySignSenderHMAC(destinationPeer),
                    ack.NhaSeq16, destinationPeer, ack.GetSignedFieldsForSenderHMAC);


                // wait for CFM from requester
                var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null,
                    requesterEndpoint,
                    RegisterConfirmationPacket.GetScanner(synReceivedFromInP2pMode, syn.RequesterPublicKey_RequestID, syn.Timestamp32S)
                    );
                var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);

                // TODO verify signatures and update quality/rating

                // send NHACK to requester
                SendNextHopAckResponseToCfm(cfm, requesterEndpoint, synReceivedFromInP2pMode);

                // send CFM to destinationPeer
                // wait for NHACK from destinationPeer, retransmit
                cfm.NhaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNHACK(cfm.Encode_OptionallySignSenderHMAC(destinationPeer),
                    cfm.NhaSeq16, destinationPeer, cfm.GetSignedFieldsForSenderHMAC);
            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxying(requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(syn.RequesterPublicKey_RequestID);
            }
        }
    }
}
