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
        async Task ProxyRegisterRequestAtEntryPeerAsync(ConnectionToNeighbor destinationPeer, RegisterSynPacket syn, IPEndPoint requesterEndpoint) // engine thread
        {
            WriteToLog_reg_proxySide_detail($"proxying registration at EP: remoteEndpoint={requesterEndpoint}, NhaSeq16={syn.NhaSeq16}, destinationPeer={destinationPeer}");
            
            _pendingRegisterRequests.Add(syn.RequesterPublicKey_RequestID);
            try
            {
                if (syn.AtoEP == false)
                {
                    //todo check hmac of proxy sender
                    throw new NotImplementedException();
                }

                if (!ValidateReceivedSynTimestamp32S(syn.Timestamp32S))
                    throw new BadSignatureException();

                syn.NumberOfHopsRemaining--;
                if (syn.NumberOfHopsRemaining == 0)
                {
                    SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero);
                    return;
                }

                // send NHACK to requester
                WriteToLog_reg_proxySide_detail($"sending NHACK to SYN requester");
                SendNextHopAckResponseToSyn(syn, requesterEndpoint);

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
                if (registerSynAckPacketData == null) throw new DrpTimeoutException();
                var synAck = RegisterSynAckPacket.DecodeAndOptionallyVerify(registerSynAckPacketData, null, null);
                WriteToLog_reg_proxySide_detail($"verified SYNACK from destinationPeer");

                // respond with NHACK
                SendNextHopAckResponseToSynAck(synAck, destinationPeer);

                // send SYNACK to requester 
                // wait for ACK / NHACK 
                // TODO in case when requester is P2P connected neighbor:   //synAck.NhaSeq16 = xx // put SYNACK.NhaSeq16, sendertoken32, senderHMAC  
                synAck.RequesterEndpoint = requesterEndpoint;
                var ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(synAck.EncodeAtResponder(null), 
                    requesterEndpoint,
                    RegisterAckPacket.GetScanner(null, syn.RequesterPublicKey_RequestID, syn.Timestamp32S)
                    );
                var ack = RegisterAckPacket.DecodeAndVerify_OptionallyInitializeP2pStreamAtResponder(ackUdpData, null, null, null);

                // send NHACK to requester
                WriteToLog_reg_proxySide_detail($"sending NHACK to ACK to requester");
                SendNextHopAckResponseToAck(ack, requesterEndpoint);
                
                // send ACK to destinationPeer
                // put ACK.NhaSeq16, sendertoken32, senderHMAC  
                // wait for NHACK
                ack.NhaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNHACK(ack.Encode_OptionallySignSenderHMAC(destinationPeer),
                    ack.NhaSeq16, destinationPeer, ack.GetSignedFieldsForSenderHMAC);


                // wait for CFM from requester
                var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null,
                    requesterEndpoint,
                    RegisterConfirmationPacket.GetScanner(null, syn.RequesterPublicKey_RequestID, syn.Timestamp32S)
                    );
                var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);

                // TODO verify signatures and update quality/rating

                // send NHACK to requester
                SendNextHopAckResponseToCfm(cfm, requesterEndpoint);

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
