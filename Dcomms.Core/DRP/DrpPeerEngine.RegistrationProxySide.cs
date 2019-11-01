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
        internal async Task<bool> ProxyRegisterRequestAsync(ReceivedRequest receivedRequest, ConnectionToNeighbor destinationPeer) // engine thread
        {
            receivedRequest.ReceivedFromNeighborNullable?.AssertIsNotDisposed();
            var req = receivedRequest.RegisterReq;
            var logger = receivedRequest.Logger;
            logger.ModuleName = VisionChannelModuleName_reg_proxySide;

            if (PendingRegisterRequestExists(req.RequesterRegistrationId))
            {
               logger.WriteToLog_higherLevelDetail($"rejecting duplicate REGISTER request {req.RequesterRegistrationId}: requesterEndpoint={receivedRequest.ReceivedFromEndpoint}");
                await receivedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return false;
            }

            if (req.NumberOfHopsRemaining > RegisterRequestPacket.MaxNumberOfHopsRemaining || req.NumberOfRandomHopsRemaining > RegisterRequestPacket.MaxNumberOfHopsRemaining)
            {
               logger.WriteToLog_needsAttention($"rejecting REGISTER request {req.RequesterRegistrationId}: invalid number of hops remaining");
                await receivedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return false;
            }

            if (!receivedRequest.CheckedRecentUniqueProxiedRegistrationRequests)
            {
                var recentUniqueProxiedRegistrationRequests = req.RandomModeAtThisHop ? RecentUniqueProxiedRegistrationRequests_RandomHop : RecentUniqueProxiedRegistrationRequests_NonRandomHop;
                if (!recentUniqueProxiedRegistrationRequests.Filter(req.GetUniqueRequestIdFields))
                {
                    logger.WriteToLog_lightPain($"rejecting non-unique REGISTER request {req.RequesterRegistrationId}: requesterEndpoint={receivedRequest.ReceivedFromEndpoint}");
                    await receivedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                    return false;
                }
                receivedRequest.CheckedRecentUniqueProxiedRegistrationRequests = true;
            }

            logger.WriteToLog_higherLevelDetail($"proxying REGISTER request: requesterEndpoint={receivedRequest.ReceivedFromEndpoint}, NumberOfHopsRemaining={req.NumberOfHopsRemaining}, ReqP2pSeq16={req.ReqP2pSeq16}, destinationPeer={destinationPeer}, sourcePeer={receivedRequest.ReceivedFromNeighborNullable}");
            
            if (!ValidateReceivedReqTimestamp64(req.ReqTimestamp64))
                throw new BadSignatureException();

            req.NumberOfHopsRemaining--;
            if (req.NumberOfHopsRemaining == 0)
            {
                logger.WriteToLog_needsAttention($"rejecting REGISTER request {req.RequesterRegistrationId}: max hops reached");
                await receivedRequest.SendErrorResponse(ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero);
                return false;
            }

            _pendingRegisterRequests.Add(req.RequesterRegistrationId);
            try
            {
                // send NPACK to source peer
                logger.WriteToLog_detail($"sending NPACK to REQ source peer (delay={(int)(DateTimeNowUtc - receivedRequest.ReqReceivedTimeUtc).TotalMilliseconds}ms)");                
                receivedRequest.SendNeighborPeerAck_accepted_IfNotAlreadyReplied();                
                                
                if (req.NumberOfRandomHopsRemaining >= 1) req.NumberOfRandomHopsRemaining--;
                logger.WriteToLog_detail($"decremented number of hops in {req}: NumberOfHopsRemaining={req.NumberOfHopsRemaining}, NumberOfRandomHopsRemaining={req.NumberOfRandomHopsRemaining}");

                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ
                req.ReqP2pSeq16 = destinationPeer.GetNewRequestP2pSeq16_P2P();
                try
                {
                    var sentRequest = new SentRequest(this, logger, destinationPeer.RemoteEndpoint, destinationPeer, req.Encode_OptionallySignNeighborHMAC(destinationPeer),
                        req.ReqP2pSeq16, RegisterAck1Packet.GetScanner(logger, req, destinationPeer));
                    await sentRequest.SendRequestAsync();

                    await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(req.Encode_OptionallySignNeighborHMAC(destinationPeer),
                        req.ReqP2pSeq16, req.GetSignedFieldsForNeighborHMAC);
                    if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                       logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 52460", req, destinationPeer.LocalDrpPeer);
                        return false;
                    }
                    if (destinationPeer?.IsDisposed == true)
                    {
                       logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 52460", req, destinationPeer.LocalDrpPeer);
                        return false;
                    }
                    if (sentRequest  == DrpResponderStatusCode.rejected_maxhopsReached)
                    {
                       logger.WriteToLog_needsAttention($"retrying with other peers on ACK1 with error={ack1.ResponderStatusCode}", req, destinationPeer.LocalDrpPeer);
                        return DrpResponderStatusCode.rejected_maxhopsReached;
                    }
                }
                catch (NextHopRejectedExceptionRouteIsUnavailable)
                {
                   logger.WriteToLog_higherLevelDetail($"got response=serviceUnavailable from destination {destinationPeer}. will try another neighbor..", req, destinationPeer.LocalDrpPeer);
                    if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                       logger.WriteToLog_needsAttention($"sourcePeer={sourcePeer} is disposed during proxying 35346232", req, destinationPeer.LocalDrpPeer);
                        return false;
                    }
                    return DrpResponderStatusCode.rejected_p2pNetworkServiceUnavailable;
                }               
                catch (Exception reqExc)
                {
                    HandleExceptionWhileProxyingRegister(req, destinationPeer.LocalDrpPeer, receivedRequest.ReceivedFromEndpoint, reqExc);
                    if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                       logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 76897805", req, destinationPeer.LocalDrpPeer);
                        return false;
                    }
                    return true;
                }

                // wait for ACK1 from destination peer
                // verify NeighborHMAC
               logger.WriteToLog_detail($"waiting for ACK1 from destination peer", req, destinationPeer.LocalDrpPeer);
                var ack1UdpData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                RegisterAck1Packet.GetScanner(req, destinationPeer),
                                    DateTimeNowUtc, Configuration.Ack1TimoutS
                                ));
                if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 1649321", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 1649321", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (ack1UdpData == null) throw new DrpTimeoutException("Did not receive ACK1 on timeout");
                var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(ack1UdpData, null, null);
               logger.WriteToLog_detail($"verified ACK1 from destination", req, destinationPeer.LocalDrpPeer);

                // respond with NPACK
                SendNeighborPeerAckResponseToRegisterAck1(ack1, destinationPeer);

               

                // send ACK1 to source peer 
                // wait for ACK / NPACK 
                if (receivedRequest.ReceivedFromNeighborNullable != null)
                {   // P2P mode
                    receivedRequest.ReceivedFromNeighborNullable.AssertIsNotDisposed();
                    ack1.ReqP2pSeq16 = receivedRequest.ReceivedFromNeighborNullable.GetNewRequestP2pSeq16_P2P();
                }
                else
                {   // A-EP mode
                    ack1.RequesterEndpoint = receivedRequest.ReceivedFromEndpoint;
                }
                var ack1UdpDataTx = ack1.Encode_OpionallySignNeighborHMAC(receivedRequest.ReceivedFromNeighborNullable);

               
               
                var ack2Scanner = RegisterAck2Packet.GetScanner(receivedRequest.ReceivedFromNeighborNullable, req);
                byte[] ack2UdpData;
                if (receivedRequest.ReceivedFromNeighborNullable == null)
                {   // A-EP mode: wait for ACK2, retransmitting ACK1
                   logger.WriteToLog_detail($"sending ACK1, waiting for ACK2", req, destinationPeer.LocalDrpPeer);
                    ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(ack1UdpDataTx, receivedRequest.ReceivedFromEndpoint, ack2Scanner);
                }
                else
                {   // P2P mode: retransmit ACK1 until NPACK (via P2P); at same time wait for ACK2
                   logger.WriteToLog_detail($"sending ACK1, awaiting for NPACK", req, destinationPeer.LocalDrpPeer);
                    _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpDataTx, receivedRequest.ReceivedFromEndpoint,
                        ack1.ReqP2pSeq16, receivedRequest.ReceivedFromNeighborNullable, ack1.GetSignedFieldsForNeighborHMAC);
                    // not waiting for NPACK, wait for ACK2
                   logger.WriteToLog_detail($"waiting for ACK2", req, destinationPeer.LocalDrpPeer);
                    ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, receivedRequest.ReceivedFromEndpoint, ack2Scanner);
                }

               logger.WriteToLog_detail($"received ACK2", req, destinationPeer.LocalDrpPeer);
                if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 2345135", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 2345135", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                var ack2 = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(ack2UdpData, null, null, null);

                // send NPACK to source peer
               logger.WriteToLog_detail($"sending NPACK to ACK2 to source peer", req, destinationPeer.LocalDrpPeer);
                SendNeighborPeerAckResponseToRegisterAck2(ack2, receivedRequest.ReceivedFromEndpoint, receivedRequest.ReceivedFromNeighborNullable);

                // send ACK2 to destination peer
                // put ACK2.ReqP2pSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                if (destinationPeer.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 345784567", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 345784567", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                ack2.ReqP2pSeq16 = destinationPeer.GetNewRequestP2pSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2.Encode_OptionallySignNeighborHMAC(destinationPeer),
                    ack2.ReqP2pSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 234646", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 234646", req, destinationPeer.LocalDrpPeer);
                    return false;
                }

                // wait for CFM from source peer
                var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null,
                    receivedRequest.ReceivedFromEndpoint,
                    RegisterConfirmationPacket.GetScanner(receivedRequest.ReceivedFromNeighborNullable, req)
                    );
                var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);
                if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 3452326", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 3452326", req, destinationPeer.LocalDrpPeer);
                    return false;
                }

                // TODO verify signatures and update QoS

                // send NPACK to source peer
                SendNeighborPeerAckResponseToRegisterCfm(cfm, receivedRequest.ReceivedFromEndpoint, receivedRequest.ReceivedFromNeighborNullable);

                // send CFM to responder
                // wait for NPACK from destination peer, retransmit
                if (destinationPeer.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 123678", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                if (receivedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={receivedRequest.ReceivedFromNeighborNullable} is disposed during proxying 123678", req, destinationPeer.LocalDrpPeer);
                    return false;
                }
                cfm.ReqP2pSeq16 = destinationPeer.GetNewRequestP2pSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfm.Encode_OptionallySignNeighborHMAC(destinationPeer),
                    cfm.ReqP2pSeq16, cfm.GetSignedFieldsForNeighborHMAC);
               

               logger.WriteToLog_higherLevelDetail($"proxying {req} is successfully complete", req, destinationPeer.LocalDrpPeer);
            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxyingRegister(req, destinationPeer.LocalDrpPeer, receivedRequest.ReceivedFromEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(req.RequesterRegistrationId);
            }

            return false;
        }

    }
}
