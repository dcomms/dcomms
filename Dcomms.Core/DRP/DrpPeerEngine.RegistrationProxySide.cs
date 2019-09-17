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
            RegisterRequestPacket req, IPEndPoint requesterEndpoint, 
            ConnectionToNeighbor sourcePeer) // engine thread
        {
            WriteToLog_reg_proxySide_detail($"proxying REGISTER request: remoteEndpoint={requesterEndpoint}, NpaSeq16={req.NpaSeq16}, destinationPeer={destinationPeer}, sourcePeer={sourcePeer}");
            
            if (req.AtoEP ^ (sourcePeer == null))
                throw new InvalidOperationException();
            _recentUniqueRegistrationRequests.AssertIsUnique(req.GetUniqueRequestIdFields);

            if (!ValidateReceivedReqTimestamp32S(req.Timestamp32S))
                throw new BadSignatureException();

            if (req.NumberOfHopsRemaining <= 1)
            {
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero, sourcePeer);
                return;
            }

            _pendingRegisterRequests.Add(req.RequesterPublicKey_RequestID);
            try
            {
                // send NPACK to source peer
                WriteToLog_reg_proxySide_detail($"sending NPACK to REQ source peer");
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.accepted, sourcePeer);

                req.NumberOfHopsRemaining--;

                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ   
                req.NpaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(req.Encode_OptionallySignSenderHMAC(destinationPeer),
                    req.NpaSeq16, req.GetSignedFieldsForSenderHMAC);

                // wait for SYNACK from destination
                // verify SenderHMAC
                WriteToLog_reg_proxySide_detail($"waiting for ACK1 from destination peer");
                var ack1UdpData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                RegisterAck1Packet.GetScanner(req.RequesterPublicKey_RequestID, req.Timestamp32S, destinationPeer),
                                    DateTimeNowUtc, Configuration.RegisterRequestsTimoutS
                                ));
                if (ack1UdpData == null) throw new DrpTimeoutException("Did not receive ACK1 on timeout");
                var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(ack1UdpData, null, null);
                WriteToLog_reg_proxySide_detail($"verified ACK1 from destination");

                // respond with NPACK
                SendNeighborPeerAckResponseToRegisterAck1(ack1, destinationPeer);

                // send SYNACK to requester 
                // wait for ACK / NPACK 
                if (sourcePeer != null)
                {   // P2P mode
                    ack1.NpaSeq16 = sourcePeer.GetNewNhaSeq16_P2P();
                }
                else
                {   // A-EP mode
                    ack1.RequesterEndpoint = requesterEndpoint;
                }
                var ack1UdpDataTx = ack1.Encode_OpionallySignSenderHMAC(sourcePeer);


                var ack2Scanner = RegisterAck2Packet.GetScanner(sourcePeer, req.RequesterPublicKey_RequestID, req.Timestamp32S);
                byte[] ack2UdpData;
                if (sourcePeer == null)
                {   // A-EP mode: wait for ACK, retransmitting SYNACK
                    WriteToLog_reg_proxySide_detail($"sending ACK1, waiting for ACK2");
                    ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(ack1UdpDataTx, requesterEndpoint, ack2Scanner);
                }
                else
                {   // P2P mode: retransmit SYNACK until NPACK (via P2P); at same time wait for ACK
                    WriteToLog_reg_proxySide_detail($"sending ACK1, awaiting for NPACK");
                    _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpDataTx, requesterEndpoint,
                        ack1.NpaSeq16, sourcePeer, ack1.GetSignedFieldsForSenderHMAC);
                    // not waiting for NPACK, wait for ACK
                    WriteToLog_reg_proxySide_detail($"waiting for ACK2");
                    ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, ack2Scanner);
                }

                WriteToLog_reg_proxySide_detail($"received ACK2");
                var ack2 = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(ack2UdpData, null, null, null);
                
                // send NPACK to source peer
                WriteToLog_reg_proxySide_detail($"sending NPACK to ACK2 to source peer");
                SendNeighborPeerAckResponseToRegisterAck2(ack2, requesterEndpoint, sourcePeer);
                
                // send ACK2 to destination peer
                // put ACK2.NpaSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                ack2.NpaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2.Encode_OptionallySignSenderHMAC(destinationPeer),
                    ack2.NpaSeq16, ack2.GetSignedFieldsForSenderHMAC);
                
                // wait for CFM from source peer
                var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null,
                    requesterEndpoint,
                    RegisterConfirmationPacket.GetScanner(sourcePeer, req.RequesterPublicKey_RequestID, req.Timestamp32S)
                    );
                var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);

                // TODO verify signatures and update QoS

                // send NPACK to source peer
                SendNeighborPeerAckResponseToRegisterCfm(cfm, requesterEndpoint, sourcePeer);

                // send CFM to responder
                // wait for NPACK from destination peer, retransmit
                cfm.NpaSeq16 = destinationPeer.GetNewNhaSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfm.Encode_OptionallySignSenderHMAC(destinationPeer),
                    cfm.NpaSeq16, cfm.GetSignedFieldsForSenderHMAC);
            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxyingRegister(requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(req.RequesterPublicKey_RequestID);
            }
        }
    }
}
