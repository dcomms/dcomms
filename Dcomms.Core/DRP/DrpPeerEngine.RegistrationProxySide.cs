using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {
        /// <summary>
        /// main register responder proc for both A-EP and P2P modes
        /// in P2P mode NeighborToken32 and NeighborHMAC are verified at this time
        /// </summary>
        /// <param name="receivedFromInP2pMode">
        /// is null in A-EP mode
        /// </param>
        /// <returns>
        /// true to retry the request with another neighbor (if the request needs to be "rerouted")
        /// </returns>
        internal async Task<bool> ProxyRegisterRequestAsync(ConnectionToNeighbor destinationPeer, 
            RegisterRequestPacket req, IPEndPoint requesterEndpoint, 
            ConnectionToNeighbor sourcePeer, bool checkRecentUniqueProxiedRegistrationRequests, DateTime reqReceivedTimeUtc
            ) // engine thread
        {
            if (req.AtoEP ^ (sourcePeer == null))
                throw new InvalidOperationException();

            if (_pendingRegisterRequests.Contains(req.RequesterRegistrationId))
            {
                WriteToLog_reg_proxySide_needsAttention($"rejecting duplicate REGISTER request {req.RequesterRegistrationId}: requesterEndpoint={requesterEndpoint}");
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.rejected_serviceUnavailable, sourcePeer);
                return false;
            }
            if (checkRecentUniqueProxiedRegistrationRequests)
            {
                if (!RecentUniqueProxiedRegistrationRequests.Filter(req.GetUniqueRequestIdFields))
                {
                    WriteToLog_reg_proxySide_lightPain($"rejecting non-unique REGISTER request {req.RequesterRegistrationId}: requesterEndpoint={requesterEndpoint}");
                    SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.rejected_serviceUnavailable, sourcePeer);
                    return false;
                }
            }

            WriteToLog_reg_proxySide_higherLevelDetail($"proxying REGISTER request: requesterEndpoint={requesterEndpoint}, NumberOfHopsRemaining={req.NumberOfHopsRemaining}, NpaSeq16={req.NpaSeq16}, destinationPeer={destinationPeer}, sourcePeer={sourcePeer}");
            
            if (!ValidateReceivedReqTimestamp64(req.ReqTimestamp64))
                throw new BadSignatureException();
            
            _pendingRegisterRequests.Add(req.RequesterRegistrationId);
            try
            {
                // send NPACK to source peer
                WriteToLog_reg_proxySide_detail($"sending NPACK to REQ source peer (delay={(int)(DateTimeNowUtc - reqReceivedTimeUtc).TotalMilliseconds}ms)");
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.accepted, sourcePeer);

                req.NumberOfHopsRemaining--;
                if (req.NumberOfHopsRemaining == 0)
                {
                    WriteToLog_reg_proxySide_needsAttention($"rejecting REGISTER request {req.RequesterRegistrationId}: max hops reached");
                    await RespondToSourcePeerWithAck1_Error(requesterEndpoint, req, sourcePeer, DrpResponderStatusCode.rejected_maxhopsReached);
                    return false;
                }

                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ
                req.NpaSeq16 = destinationPeer.GetNewNpaSeq16_P2P();
                try
                {
                    await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(req.Encode_OptionallySignNeighborHMAC(destinationPeer),
                        req.NpaSeq16, req.GetSignedFieldsForNeighborHMAC);
                }
                catch (NextHopRejectedExceptionServiceUnavailable)
                {
                    WriteToLog_reg_proxySide_needsAttention($"got response=serviceUnavailable from destination {destinationPeer}");
                    return true;
                }               
                catch (Exception reqExc)
                {
                    HandleExceptionWhileProxyingRegister(requesterEndpoint, reqExc);
                    return true;
                }

                // wait for ACK1 from destination peer
                // verify NeighborHMAC
                WriteToLog_reg_proxySide_detail($"waiting for ACK1 from destination peer");
                var ack1UdpData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                RegisterAck1Packet.GetScanner(req.RequesterRegistrationId, req.ReqTimestamp64, destinationPeer),
                                    DateTimeNowUtc, Configuration.RegisterRequestsTimoutS
                                ));
                if (ack1UdpData == null) throw new DrpTimeoutException("Did not receive ACK1 on timeout");
                var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(ack1UdpData, null, null);
                WriteToLog_reg_proxySide_detail($"verified ACK1 from destination");

                // respond with NPACK
                SendNeighborPeerAckResponseToRegisterAck1(ack1, destinationPeer);

                if (ack1.ResponderStatusCode == DrpResponderStatusCode.rejected_p2pNetworkServiceUnavailable)
                {
                    WriteToLog_reg_proxySide_needsAttention($"retrying with other peers on ACK1 with error={ack1.ResponderStatusCode}");
                    return true;
                }

                // send ACK1 to source peer 
                // wait for ACK / NPACK 
                if (sourcePeer != null)
                {   // P2P mode
                    sourcePeer.AssertIsNotDisposed();
                    ack1.NpaSeq16 = sourcePeer.GetNewNpaSeq16_P2P();
                }
                else
                {   // A-EP mode
                    ack1.RequesterEndpoint = requesterEndpoint;
                }
                var ack1UdpDataTx = ack1.Encode_OpionallySignNeighborHMAC(sourcePeer);

                if (ack1.ResponderStatusCode != DrpResponderStatusCode.confirmed)
                {
                    // terminate the async operation here, if response code is ERROR = num_hops  reached zero
                    WriteToLog_reg_proxySide_needsAttention($"sending ACK1 with error={ack1.ResponderStatusCode}, waiting for NPACK");
                    await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpDataTx, requesterEndpoint,
                        ack1.NpaSeq16, sourcePeer, ack1.GetSignedFieldsForNeighborHMAC);
                }
                else
                {
                    var ack2Scanner = RegisterAck2Packet.GetScanner(sourcePeer, req.RequesterRegistrationId, req.ReqTimestamp64);
                    byte[] ack2UdpData;
                    if (sourcePeer == null)
                    {   // A-EP mode: wait for ACK2, retransmitting ACK1
                        WriteToLog_reg_proxySide_detail($"sending ACK1, waiting for ACK2");
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(ack1UdpDataTx, requesterEndpoint, ack2Scanner);
                    }
                    else
                    {   // P2P mode: retransmit ACK1 until NPACK (via P2P); at same time wait for ACK2
                        WriteToLog_reg_proxySide_detail($"sending ACK1, awaiting for NPACK");
                        _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpDataTx, requesterEndpoint,
                            ack1.NpaSeq16, sourcePeer, ack1.GetSignedFieldsForNeighborHMAC);
                        // not waiting for NPACK, wait for ACK2
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
                    ack2.NpaSeq16 = destinationPeer.GetNewNpaSeq16_P2P();
                    await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2.Encode_OptionallySignNeighborHMAC(destinationPeer),
                        ack2.NpaSeq16, ack2.GetSignedFieldsForNeighborHMAC);

                    // wait for CFM from source peer
                    var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null,
                        requesterEndpoint,
                        RegisterConfirmationPacket.GetScanner(sourcePeer, req.RequesterRegistrationId, req.ReqTimestamp64)
                        );
                    var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);

                    // TODO verify signatures and update QoS

                    // send NPACK to source peer
                    SendNeighborPeerAckResponseToRegisterCfm(cfm, requesterEndpoint, sourcePeer);

                    // send CFM to responder
                    // wait for NPACK from destination peer, retransmit
                    cfm.NpaSeq16 = destinationPeer.GetNewNpaSeq16_P2P();
                    await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfm.Encode_OptionallySignNeighborHMAC(destinationPeer),
                        cfm.NpaSeq16, cfm.GetSignedFieldsForNeighborHMAC);
                }

                WriteToLog_reg_proxySide_higherLevelDetail($"proxying REGISTER request {req.RequesterRegistrationId} is complete");
            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxyingRegister(requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(req.RequesterRegistrationId);
            }

            return false;
        }
        async Task RespondToSourcePeerWithAck1_Error(IPEndPoint requesterEndpoint, RegisterRequestPacket req, 
            ConnectionToNeighbor sourcePeer, DrpResponderStatusCode errorCode)
        {
            var localDrpPeerThatRejectsRequest = sourcePeer?.LocalDrpPeer ?? this.LocalPeers.Values.First();

            var ack1 = new RegisterAck1Packet
            {
                RequesterRegistrationId = req.RequesterRegistrationId,
                ReqTimestamp64 = req.ReqTimestamp64,
                ResponderStatusCode = errorCode,
                ResponderRegistrationId = localDrpPeerThatRejectsRequest.Configuration.LocalPeerRegistrationId,
            };

            ack1.NpaSeq16 = sourcePeer?.GetNewNpaSeq16_P2P() ?? this.GetNewNpaSeq16_AtoEP();           
          
            ack1.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                (w2) =>
                {
                    req.GetSharedSignedFields(w2, true);
                    ack1.GetSharedSignedFields(w2, false, true);
                },
                localDrpPeerThatRejectsRequest.Configuration.LocalPeerRegistrationPrivateKey);


            var ack1UdpData = ack1.Encode_OpionallySignNeighborHMAC(sourcePeer);
            await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpData, requesterEndpoint,
                        ack1.NpaSeq16, sourcePeer, ack1.GetSignedFieldsForNeighborHMAC);

        }
    }
}
