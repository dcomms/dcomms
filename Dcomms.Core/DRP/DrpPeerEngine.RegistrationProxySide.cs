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
        internal async Task<bool> ProxyRegisterRequestAsync(RoutedRequest routedRequest, ConnectionToNeighbor destinationPeer) // engine thread
        {
            routedRequest.ReceivedFromNeighborNullable?.AssertIsNotDisposed();
            var req = routedRequest.RegisterReq;
            var logger = routedRequest.Logger;
            logger.ModuleName = VisionChannelModuleName_reg_proxySide;

            if (PendingRegisterRequestExists(req.RequesterRegistrationId))
            {
               logger.WriteToLog_higherLevelDetail($"rejecting duplicate request {req}: requesterEndpoint={routedRequest.ReceivedFromEndpoint}");
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return false;
            }

            if (req.NumberOfHopsRemaining > RegisterRequestPacket.MaxNumberOfHopsRemaining || req.NumberOfRandomHopsRemaining > RegisterRequestPacket.MaxNumberOfHopsRemaining)
            {
               logger.WriteToLog_needsAttention($"rejecting {req}: invalid number of hops remaining");
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return false;
            }

            if (!routedRequest.CheckedRecentUniqueProxiedRequests)
            {
                var recentUniqueProxiedRegistrationRequests = req.RandomModeAtThisHop ? RecentUniqueProxiedRegistrationRequests_RandomHop : RecentUniqueProxiedRegistrationRequests_NonRandomHop;
                if (!recentUniqueProxiedRegistrationRequests.Filter(req.GetUniqueRequestIdFields))
                {
                    logger.WriteToLog_higherLevelDetail($"rejecting non-unique request {req}: requesterEndpoint={routedRequest.ReceivedFromEndpoint}");
                    await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                    return false;
                }
                routedRequest.CheckedRecentUniqueProxiedRequests = true;
            } 

            logger.WriteToLog_higherLevelDetail($"proxying {req}: requesterEndpoint={routedRequest.ReceivedFromEndpoint}, NumberOfHopsRemaining={req.NumberOfHopsRemaining}, ReqP2pSeq16={req.ReqP2pSeq16}, destinationPeer={destinationPeer}, sourcePeer={routedRequest.ReceivedFromNeighborNullable}");
            
            if (!ValidateReceivedReqTimestamp64(req.ReqTimestamp64))
                throw new BadSignatureException();

            req.NumberOfHopsRemaining--;
            if (req.NumberOfHopsRemaining == 0)
            {
                logger.WriteToLog_needsAttention($"rejecting REGISTER request {req.RequesterRegistrationId}: max hops reached");
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero);
                return false;
            }

            _pendingRegisterRequests.Add(req.RequesterRegistrationId);
            try
            {
                // send NPACK to source peer
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to REQ source peer (delay={(int)(DateTimeNowUtc - routedRequest.ReqReceivedTimeUtc.Value).TotalMilliseconds}ms)");                
                routedRequest.SendNeighborPeerAck_accepted_IfNotAlreadyReplied();                
                                
                if (req.NumberOfRandomHopsRemaining >= 1) req.NumberOfRandomHopsRemaining--;
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"decremented number of hops in {req}: NumberOfHopsRemaining={req.NumberOfHopsRemaining}, NumberOfRandomHopsRemaining={req.NumberOfRandomHopsRemaining}");

                req.ReqP2pSeq16 = destinationPeer.GetNewRequestP2pSeq16_P2P();
                byte[] ack1UdpData;
                try
                {
                    var sentRequest = new SentRequest(this, logger, destinationPeer.RemoteEndpoint, destinationPeer, req.Encode_OptionallySignNeighborHMAC(destinationPeer),
                        req.ReqP2pSeq16, RegisterAck1Packet.GetScanner(logger, req, destinationPeer));

                    // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ
                    // wait for ACK1 from destination peer
                    // verify NeighborHMAC
                    // var ack1R = await sentRequest.SendRequestAsync_NoExc("ack1 34601"); ////////////////////////////////// new method
                    ack1UdpData = await sentRequest.SendRequestAsync("ack1 34601");
                  //  if (logger.WriteToLog_detail2_enabled) logger.WriteToLog_detail("got ACK1 result");

                    if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                        logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 52460");
                        return false;
                    }
                    if (destinationPeer?.IsDisposed == true)
                    {
                        logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 52460");
                        return false;
                    }

                    //if (ack1R.ResponseCode != ResponseOrFailureCode.accepted)
                    //{
                    //    logger.WriteToLog_higherLevelDetail($"got response={ack1R.ResponseCode} from destination {destinationPeer}");
                    //    if (ack1R.ResponseCode == ResponseOrFailureCode.failure_routeIsUnavailable)
                    //        req.NumberOfHopsRemaining++; // roll back previous decrement for a new trial                      
                    //    return true; // will retry
                    //}
                    //ack1UdpData = ack1R.UdpData;
                }
                catch (RequestRejectedException reqExc)
                {
                    logger.WriteToLog_higherLevelDetail($"got exception: response={reqExc.ResponseCode} from destination {destinationPeer}");
                    if (reqExc.ResponseCode == ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero)
                    {
                        await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero);
                        return false;
                    }

                    if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                        logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 35346232");
                        return false;
                    }
                    if (reqExc.ResponseCode == ResponseOrFailureCode.failure_routeIsUnavailable)
                    {
                        req.NumberOfHopsRemaining++; // roll back previous decrement for a new trial
                    }

                    return true; // will retry
                }
                catch (DrpTimeoutException)
                {
                    logger.WriteToLog_higherLevelDetail($"got timeout error when requesting {destinationPeer}");
                    req.NumberOfHopsRemaining++; // roll back previous decrement for a new trial
                    return true; // will retry
                }
                catch (Exception reqExc)
                {
                    logger.WriteToLog_mediumPain($"could not proxy REGISTER request: {reqExc}");
                    if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                        logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 76897805");
                        return false;
                    }
                    return true; // will retry
                }

                var tr1 = CreateTracker("ack1 34601 2");
                var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(logger, ack1UdpData, req, null);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified ACK1 from destination");

                // respond with NPACK
                SendNeighborPeerAckResponseToRegisterAck1(ack1, destinationPeer);
                
                // send ACK1 to source peer 
                // wait for ACK / NPACK 
                if (routedRequest.ReceivedFromNeighborNullable != null)
                {   // P2P mode
                    routedRequest.ReceivedFromNeighborNullable.AssertIsNotDisposed();
                    ack1.ReqP2pSeq16 = routedRequest.ReceivedFromNeighborNullable.GetNewRequestP2pSeq16_P2P();
                }
                else
                {   // A-EP mode
                    ack1.RequesterEndpoint = routedRequest.ReceivedFromEndpoint;
                }
                var ack1UdpDataTx = ack1.Encode_OpionallySignNeighborHMAC(routedRequest.ReceivedFromNeighborNullable);

                var sourcePeerVisibleDescription = routedRequest.ReceivedFromNeighborNullable?.ToString() ?? routedRequest.ReceivedFromEndpoint.ToString();
                var ack2Scanner = RegisterAck2Packet.GetScanner(logger, routedRequest.ReceivedFromNeighborNullable, req);
                tr1.Dispose();
                byte[] ack2UdpData;
                if (routedRequest.ReceivedFromNeighborNullable == null)
                {   // A-EP mode: wait for ACK2, retransmitting ACK1
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK1, waiting for ACK2");
                    ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("ack2 12368", sourcePeerVisibleDescription,
                        ack1UdpDataTx, routedRequest.ReceivedFromEndpoint, ack2Scanner);
                }
                else
                {   // P2P mode: retransmit ACK1 until NPACK (via P2P); at same time wait for ACK2
                   if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK1, awaiting for NPACK");
                    _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("ack1 3686", ack1UdpDataTx, routedRequest.ReceivedFromEndpoint,
                        ack1.ReqP2pSeq16, routedRequest.ReceivedFromNeighborNullable, ack1.GetSignedFieldsForNeighborHMAC);
                    // not waiting for NPACK, wait for ACK2
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for ACK2");
                    ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("ack2 345209", sourcePeerVisibleDescription, 
                        null, routedRequest.ReceivedFromEndpoint, ack2Scanner);
                }

                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received ACK2");
                if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 2345135");
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 2345135");
                    return false;
                }
                var ack2 = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(logger, ack2UdpData, null, null, null);

                // send NPACK to source peer
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to ACK2 to source peer");
                SendNeighborPeerAckResponseToRegisterAck2(ack2, routedRequest.ReceivedFromEndpoint, routedRequest.ReceivedFromNeighborNullable);

                // send ACK2 to destination peer
                // put ACK2.ReqP2pSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                if (destinationPeer.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 345784567");
                    return false;
                }
                if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 345784567");
                    return false;
                }
                ack2.ReqP2pSeq16 = destinationPeer.GetNewRequestP2pSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK("ack2 41937", ack2.Encode_OptionallySignNeighborHMAC(destinationPeer),
                    ack2.ReqP2pSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 234646");
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 234646");
                    return false;
                }

                // wait for CFM from source peer
                var cfmUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("cfm 234789", sourcePeerVisibleDescription, null,
                    routedRequest.ReceivedFromEndpoint,
                    RegisterConfirmationPacket.GetScanner(logger, routedRequest.ReceivedFromNeighborNullable, req)
                    );
                var cfm = RegisterConfirmationPacket.DecodeAndOptionallyVerify(cfmUdpData, null, null);
                if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                    logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 3452326");
                    return false;
                }
                if (destinationPeer?.IsDisposed == true)
                {
                    logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 3452326");
                    return false;
                }

                // TODO verify signatures and update QoS

                // send NPACK to source peer
                SendNeighborPeerAckResponseToRegisterCfm(cfm, routedRequest.ReceivedFromEndpoint, routedRequest.ReceivedFromNeighborNullable);

                // send CFM to responder
                // wait for NPACK from destination peer, retransmit
                if (destinationPeer.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"destinationPeer={destinationPeer} is disposed during proxying 123678");
                    return false;
                }
                if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                {
                   logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 123678");
                    return false;
                }
                cfm.ReqP2pSeq16 = destinationPeer.GetNewRequestP2pSeq16_P2P();
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK("cfm 1357", cfm.Encode_OptionallySignNeighborHMAC(destinationPeer),
                    cfm.ReqP2pSeq16, cfm.GetSignedFieldsForNeighborHMAC);
               

                logger.WriteToLog_higherLevelDetail($"proxying {req} is successfully complete");
            }
            catch (DrpTimeoutException)
            {
                logger.WriteToLog_lightPain($"could not proxy REGISTER: request timeout");
            }
            catch (Exception exc)
            {
                logger.WriteToLog_mediumPain($"could not proxy REGISTER request: {exc}");
            }
            finally
            {
                _pendingRegisterRequests.Remove(req.RequesterRegistrationId);
            }

            return false;
        }

    }
}
