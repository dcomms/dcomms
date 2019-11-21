using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {
        /// <summary>
        /// list of currently proxied or accepted INVITE requester regIDs
        /// protects local per from processing same (retransmitted) REQ packet, also from spam from a single requester
        /// protects the P2P network against looped REQ requests
        /// </summary>
        HashSet<RegistrationId> _pendingInviteRequests = new HashSet<RegistrationId>();
        public bool PendingInviteRequestExists(RegistrationId requesterRegId) => _pendingInviteRequests.Contains(requesterRegId);

        /// <summary>
        /// Timestamp32S, NeighborToken32 and NeighborHMAC are verified at this time
        /// </summary>
        /// <returns>
        /// true to retry the request with another neighbor (if the request needs to be "rerouted")
        /// </returns>
        internal async Task<bool> ProxyInviteRequestAsync(RoutedRequest routedRequest, ConnectionToNeighbor destinationPeer)
        {
            var req = routedRequest.InviteReq;
            var logger = routedRequest.Logger;
            logger.ModuleName = DrpPeerEngine.VisionChannelModuleName_inv_proxySide;         
            if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"proxying {req} to {destinationPeer}");

            if (!routedRequest.CheckedRecentUniqueProxiedRequests)
            {
                if (!Engine.RecentUniqueInviteRequests.Filter(req.GetUniqueRequestIdFields))
                {
                    logger.WriteToLog_higherLevelDetail($"rejecting non-unique {req}: requesterEndpoint={routedRequest.ReceivedFromEndpoint}");
                    await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                    return false;
                }
                routedRequest.CheckedRecentUniqueProxiedRequests = true;
            }
            
            if (req.NumberOfHopsRemaining > InviteRequestPacket.MaxNumberOfHopsRemaining)
            {
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return false;
            }
            if (req.NumberOfHopsRemaining <= 1)
            {
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero);
                return false;
            }

            _pendingInviteRequests.Add(req.RequesterRegistrationId);
            try
            {
                // send NPACK to REQ
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to REQ source peer");
                routedRequest.SendNeighborPeerAck_accepted_IfNotAlreadyReplied();

                req.NumberOfHopsRemaining--;
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"decremented number of hops in {req}: NumberOfHopsRemaining={req.NumberOfHopsRemaining}");
                
                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ   
                var reqUdpData = req.Encode_SetP2pFields(destinationPeer);

                #region wait for ACK1 from responder  verify NeighborHMAC
        
                byte[] ack1UdpData;
                try
                {
                    var sentRequest = new SentRequest(Engine, logger, destinationPeer.RemoteEndpoint, destinationPeer, reqUdpData,
                        req.ReqP2pSeq16, InviteAck1Packet.GetScanner(logger, req, destinationPeer));
                    ack1UdpData = await sentRequest.SendRequestAsync("inv proxy ack1 3457");
                }               
                catch (RequestRejectedException reqExc)
                {
                    logger.WriteToLog_higherLevelDetail($"got response={reqExc.ResponseCode} from destination {destinationPeer}. will try another neighbor..");
                    
                    if (reqExc.ResponseCode == ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero)
                    {
                        await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_numberOfHopsRemainingReachedZero);
                        return false;
                    }

                    if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                        logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 75675");
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
                    logger.WriteToLog_mediumPain($"could not proxy INVITE request: {reqExc}");
                    if (routedRequest.ReceivedFromNeighborNullable?.IsDisposed == true)
                    {
                        logger.WriteToLog_needsAttention($"sourcePeer={routedRequest.ReceivedFromNeighborNullable} is disposed during proxying 76897805");
                        return false;
                    }
                    return true; // will retry
                }


                var ack1 = InviteAck1Packet.Decode(ack1UdpData);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified ACK1 from responder");
                // respond with NPACK to ACk1
                SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
                #endregion

                #region send ACK1 to requester, wait for NPACK and ACK2
                var ack1UdpDataTx = ack1.Encode_SetP2pFields(routedRequest.ReceivedFromNeighborNullable);

                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK1, awaiting for NPACK");
                _ = Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("ack1 13536", ack1UdpDataTx, routedRequest.ReceivedFromEndpoint,
                    ack1.ReqP2pSeq16, routedRequest.ReceivedFromNeighborNullable, ack1.GetSignedFieldsForNeighborHMAC);
                // not waiting for NPACK, wait for ACK1
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for ACK2");

                var ack2UdpData = await Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("ack2 2346892",
                    routedRequest.ReceivedFromNeighborNullable?.ToString() ?? routedRequest.ReceivedFromEndpoint.ToString(), 
                    null, routedRequest.ReceivedFromEndpoint, 
                    InviteAck2Packet.GetScanner(logger, req, routedRequest.ReceivedFromNeighborNullable));
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received ACK2");
                var ack2 = InviteAck2Packet.Decode(ack2UdpData);
                #endregion

                // send NPACK to ACK2
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to ACK2 to source peer");
                SendNeighborPeerAckResponseToAck2(ack2, routedRequest.ReceivedFromNeighborNullable);

                // send ACK2 to responder
                // put ACK2.ReqP2pSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                var ack2UdpDataTx = ack2.Encode_SetP2pFields(destinationPeer);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK2 to responder");
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK("ack2 5344530", ack2UdpDataTx,
                    ack2.ReqP2pSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received NPACK to ACK2 from destination peer");

                // wait for CFM from responder
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for CFM from responder");
                var cfmUdpData = await Engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest("cfm 12358", destinationPeer.RemoteEndpoint,
                                InviteConfirmationPacket.GetScanner(logger, req, destinationPeer),
                                    Engine.DateTimeNowUtc, Engine.Configuration.CfmTimoutS
                                ));
                if (cfmUdpData == null) throw new DrpTimeoutException($"inv. proxy CFM response from destination peer {destinationPeer} (timeout={Engine.Configuration.CfmTimoutS}s)");
                var cfm = InviteConfirmationPacket.Decode(cfmUdpData);
                // todo verify signature, update RDRs and QoS
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified CFM from responder");

                // respond NPACK to CFM to destination peer
                SendNeighborPeerAckResponseToCfm(cfm, destinationPeer);

                // send CFM to requester
                var cfmUdpDataTx = cfm.Encode_SetP2pFields(routedRequest.ReceivedFromNeighborNullable);

                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending CFM to requester, waiting for NPACK");
                await Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("cfm 23468", cfmUdpDataTx, routedRequest.ReceivedFromEndpoint,
                    cfm.ReqP2pSeq16, routedRequest.ReceivedFromNeighborNullable, cfm.GetSignedFieldsForNeighborHMAC);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received NPACK to CFM from source peer");
            }
            catch (DrpTimeoutException exc)
            {
                logger.WriteToLog_lightPain($"could not proxy INVITE: {exc.Message}");
            }
            catch (Exception exc)
            {
                logger.WriteToLog_mediumPain($"could not proxy INVITE request: {exc}");
            }
            finally
            {
                _pendingInviteRequests.Remove(req.RequesterRegistrationId);
            }
            return false;
        }
    }
}
