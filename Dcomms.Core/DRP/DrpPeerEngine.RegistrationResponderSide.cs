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
        internal async Task ProcessRegisterRequestAsync(LocalDrpPeer receivedAtLocalDrpPeerNullable, RoutedRequest routedRequest)
        {
            var req = routedRequest.RegisterReq;
            if (!ValidateReceivedReqTimestamp64(req.ReqTimestamp64))
                throw new BadSignatureException($"invalid REGISTER REQ ReqTimestamp64={MiscProcedures.Int64ticksToDateTime(req.ReqTimestamp64)} 265");

            if (PendingRegisterRequestExists(req.RequesterRegistrationId))
            {
                routedRequest.Logger.WriteToLog_higherLevelDetail($"rejecting {req}: another request is already pending");             
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return;
            }


        _retry:
            routedRequest.ReceivedFromNeighborNullable?.AssertIsNotDisposed();
            if (!RouteRegistrationRequest(receivedAtLocalDrpPeerNullable, routedRequest, out var proxyToDestinationPeer, out var acceptAt)) // routing
            { // no route found
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                return;
            }

            if (acceptAt != null)
            {   // accept the registration request here at this.LocalDrpPeer     
                routedRequest.ReceivedFromNeighborNullable?.AssertIsNotDisposed();
                _ = AcceptRegisterRequestAsync(acceptAt, routedRequest);
            }
            else if (proxyToDestinationPeer != null)
            {  // proxy the registration request to another peer
                routedRequest.ReceivedFromNeighborNullable?.AssertIsNotDisposed();
                var needToRerouteToAnotherNeighbor = await ProxyRegisterRequestAsync(routedRequest, proxyToDestinationPeer);
               
                if (needToRerouteToAnotherNeighbor && routedRequest.ReceivedFromNeighborNullable?.IsDisposed != true)
                {
                    routedRequest.TriedNeighbors.Add(proxyToDestinationPeer);
                    routedRequest.Logger.WriteToLog_detail($"retrying to proxy registration to another neighbor on error. already tried {routedRequest.TriedNeighbors.Count}");                  
                    routedRequest.ReceivedFromNeighborNullable?.AssertIsNotDisposed();
                    goto _retry;
                }
            }
            else
            {
                routedRequest.Logger.WriteToLog_detail($"rejecting request: routing failed");
                await routedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
            }
        }



        /// <summary>
        /// main register responder proc for both A-EP and P2P modes
        /// in P2P mode Timestamp32S, NeighborToken32 and NeighborHMAC are verified at this time
        /// </summary>
        /// <param name="receivedFromInP2pMode">
        /// is null in A-EP mode
        /// </param>
        internal async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RoutedRequest routedRequest) // engine thread
        {
            var logger = routedRequest.Logger;
            logger.ModuleName = VisionChannelModuleName_reg_responderSide;
            var req = routedRequest.RegisterReq;
            if (req.RequesterRegistrationId.Equals(acceptAt.Configuration.LocalPeerRegistrationId)) throw new InvalidOperationException();

            // check  signature of requester (A)
            if (!req.RequesterSignature.Verify(_cryptoLibrary,
                        w => req.GetSharedSignedFields(w, false),
                        req.RequesterRegistrationId
                    )
                )
                throw new BadSignatureException("invalid REEGISTER REQ RequesterSignature 2396");

            if (routedRequest.ReceivedFromNeighborNullable == null)
            { // A-EP mode
                if (req.EpEndpoint.Address.Equals(acceptAt.PublicIpApiProviderResponse) == false)
                {
                    throw new PossibleAttackException();
                }
            }

            if (PendingRegisterRequestExists(req.RequesterRegistrationId))
            {
                // received duplicate REGISTER REQ packet
                logger.WriteToLog_needsAttention($"ignoring duplicate registration request {req.RequesterRegistrationId} from {routedRequest.ReceivedFromEndpoint}");
                return;
            }

            if (!RecentUniqueAcceptedRegistrationRequests.Filter(req.GetUniqueRequestIdFields))
            {
                logger.WriteToLog_needsAttention($"ignoring registration request {req.RequesterRegistrationId} ts={req.ReqTimestamp64} from {routedRequest.ReceivedFromEndpoint} with non-unique request ID fields");
                return;
            }

            logger.WriteToLog_higherLevelDetail($"accepting registration from {routedRequest.ReceivedFromEndpoint}: ReqP2pSeq16={req.ReqP2pSeq16}, NumberOfHopsRemaining={req.NumberOfHopsRemaining}, epEndpoint={req.EpEndpoint}, sourcePeer={routedRequest.ReceivedFromNeighborNullable}, ts={req.ReqTimestamp64}");

            if (!RecentUniquePublicEcdhKeys.Filter(req.RequesterEcdhePublicKey.Ecdh25519PublicKey))
            {
                logger.WriteToLog_needsAttention($"ignoring registration request {req.RequesterRegistrationId} from {routedRequest.ReceivedFromEndpoint} with non-unique RequesterEcdhePublicKey");
                return;
            }

            _pendingRegisterRequests.Add(req.RequesterRegistrationId);
            try
            {
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to REQ to {routedRequest.ReceivedFromEndpoint} (delay={(int)(DateTimeNowUtc - routedRequest.ReqReceivedTimeUtc.Value).TotalMilliseconds}ms)");
                routedRequest.SendNeighborPeerAck_accepted_IfNotAlreadyReplied();

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer, req.RequesterRegistrationId)
                {
                    LocalEndpoint = routedRequest.ReceivedFromNeighborNullable?.LocalEndpoint ?? req.EpEndpoint,
                };
                byte[] ack1UdpData;
                try
                {
                    var ack1 = new RegisterAck1Packet
                    {                        
                        RequesterRegistrationId = req.RequesterRegistrationId,
                        ReqTimestamp64 = req.ReqTimestamp64,
                        ResponderEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                        ResponderRegistrationId = acceptAt.Configuration.LocalPeerRegistrationId,
                        ReqP2pSeq16 = GetNewNpaSeq16_AtoEP(),
                    };
                    RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey, $"ack1.ResponderEcdhePublicKey");
                    ack1.ToResponderTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack1_ToResponderTxParametersEncrypted_AtResponder_DeriveSharedDhSecret(logger, req, ack1, routedRequest.ReceivedFromNeighborNullable);
                    ack1.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        (w2) =>
                        {
                            req.GetSharedSignedFields(w2, true);
                            ack1.GetSharedSignedFields(w2, false, true);
                        },
                        acceptAt.Configuration.LocalPeerRegistrationPrivateKey);
                    if (routedRequest.ReceivedFromNeighborNullable == null) ack1.RequesterEndpoint = routedRequest.ReceivedFromEndpoint;                    
                    ack1UdpData = ack1.Encode_OpionallySignNeighborHMAC(routedRequest.ReceivedFromNeighborNullable);
                    
                    var ack2Scanner = RegisterAck2Packet.GetScanner(logger, routedRequest.ReceivedFromNeighborNullable, req);
                    var requesterVisibleDescription = routedRequest.ReceivedFromNeighborNullable?.ToString() ?? routedRequest.ReceivedFromEndpoint.ToString();
                    byte[] ack2UdpData;
                    if (routedRequest.ReceivedFromNeighborNullable == null)
                    {   // wait for ACK2, retransmitting ACK1
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK1, waiting for ACK2");
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("ack2 33469", requesterVisibleDescription, ack1UdpData, routedRequest.ReceivedFromEndpoint, ack2Scanner);
                    }
                    else
                    {   // retransmit ACK1 until NPACK (via P2P); at same time wait for ACK
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK1, awaiting for NPACK");
                        _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("ack1 423087", ack1UdpData, routedRequest.ReceivedFromEndpoint,
                            ack1.ReqP2pSeq16, routedRequest.ReceivedFromNeighborNullable, ack1.GetSignedFieldsForNeighborHMAC);
                        // not waiting for NPACK, wait for ACK
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for ACK2");                        
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("ack2 46051", requesterVisibleDescription, null, routedRequest.ReceivedFromEndpoint, ack2Scanner);                
                    }

                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received ACK2");
                    var ack2 = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(logger, ack2UdpData, req, ack1, newConnectionToNeighbor);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified ACK2");

                    acceptAt.AddToConnectedNeighbors(newConnectionToNeighbor, req); // added to list here in order to respond to ping requests from A    

                    SendNeighborPeerAckResponseToRegisterAck2(ack2, routedRequest.ReceivedFromEndpoint, routedRequest.ReceivedFromNeighborNullable); // send NPACK to ACK

                    _ = WaitForRegistrationConfirmationRequestAsync(requesterVisibleDescription, logger, routedRequest.ReceivedFromEndpoint, req, newConnectionToNeighbor, routedRequest.ReceivedFromNeighborNullable);

                    #region send ping, verify pong
                    var ping = newConnectionToNeighbor.CreatePing(true, false, acceptAt.ConnectedNeighborsBusySectorIds, acceptAt.AnotherNeighborToSameSectorExists(newConnectionToNeighbor));
                    
                    var pendingPingRequest = new PendingLowLevelUdpRequest("pendingPingRequest 693", newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, ping.PingRequestId32), DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    ping.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sent PING");
                    var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pong from A
                    if (pongPacketData == null) throw new DrpTimeoutException($"reg. responder initial PING request to {newConnectionToNeighbor} (timeout={Configuration.InitialPingRequests_ExpirationTimeoutS}s)");
                    var pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                        pongPacketData, ping, newConnectionToNeighbor,
                        true);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified PONG");
                    newConnectionToNeighbor.OnReceivedVerifiedPong(pong, pendingPingRequest.ResponseReceivedAtUtc.Value,
                        pendingPingRequest.ResponseReceivedAtUtc.Value - pendingPingRequest.InitialTxTimeUTC.Value);
                    #endregion
                }
                catch (Exception exc)
                {
                    newConnectionToNeighbor.Dispose();
                    throw exc;
                }
            }
            catch (DrpTimeoutException exc)
            {
                logger.WriteToLog_needsAttention($"could not accept REGISTER request: {exc}");
            }
            catch (Exception exc)
            {
                logger.WriteToLog_mediumPain($"could not accept REGISTER request: {exc}");
            }
            finally
            {
                _pendingRegisterRequests.Remove(req.RequesterRegistrationId);
            }
        }
        async Task WaitForRegistrationConfirmationRequestAsync(string requesterVisibleDescription, Logger logger, IPEndPoint requesterEndpoint, RegisterRequestPacket req, ConnectionToNeighbor newConnectionToNeighbor, 
            ConnectionToNeighbor sourcePeerNullable)
        {
            try
            {
                var regCfmScanner = RegisterConfirmationPacket.GetScanner(logger, sourcePeerNullable, req);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for CFM");
                var regCfmUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("cfm 123575", requesterVisibleDescription, null, requesterEndpoint, regCfmScanner, Configuration.CfmTimoutS);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received CFM");
                var registerCfmPacket = RegisterConfirmationPacket.DecodeAndOptionallyVerify(regCfmUdpPayload, req, newConnectionToNeighbor);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified CFM");

                SendNeighborPeerAckResponseToRegisterCfm(registerCfmPacket, requesterEndpoint, sourcePeerNullable);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sent NPACK to CFM");
            }
			catch (Exception exc)
            {
                logger.WriteToLog_lightPain($"disposing new connection because of CFM error: {exc}");
                newConnectionToNeighbor.Dispose();
            }
        }

        //internal void SendErrorResponseToRegisterReq(RegisterRequestPacket req, IPEndPoint requesterEndpoint, 
        //    ConnectionToNeighbor neighbor, bool alreadyRepliedWithNPA, DrpResponderStatusCode errorCode)
        //{
        //    WriteToLog_routing_higherLevelDetail($"routing failed, executing SendErrorResponseToRegisterReq()", req, neighbor?.LocalDrpPeer);
        //    if (alreadyRepliedWithNPA)
        //    {
        //        // send ack1
        //        _ = RespondToSourcePeerWithAck1_Error(requesterEndpoint, req, neighbor, errorCode);
        //    }
        //    else
        //    {
        //        // send NPACK
        //        SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseOrFailureCode.failure_routeIsUnavailable, neighbor);
        //    }
        //}

        internal void SendNeighborPeerAckResponseToRegisterAck1(RegisterAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ack1.ReqP2pSeq16,
                ResponseCode = ResponseOrFailureCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, ack1.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            RespondToRequestAndRetransmissions(ack1.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);
        }
        void SendNeighborPeerAckResponseToRegisterAck2(RegisterAck2Packet ack2, IPEndPoint remoteEndpoint, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ack2.ReqP2pSeq16,
                ResponseCode = ResponseOrFailureCode.accepted
            };
            if (ack2.AtoEP == false)
            {
                npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
                npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, ack2.GetSignedFieldsForNeighborHMAC));
            }

            var npAckUdpData = npAck.Encode(ack2.AtoEP);
            RespondToRequestAndRetransmissions(ack2.DecodedUdpPayloadData, npAckUdpData, remoteEndpoint);
        }
        void SendNeighborPeerAckResponseToRegisterCfm(RegisterConfirmationPacket cfm, IPEndPoint remoteEndpoint, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = cfm.ReqP2pSeq16,
                ResponseCode = ResponseOrFailureCode.accepted
            };
            if (cfm.AtoEP == false)
            {
                npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
                npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, cfm.GetSignedFieldsForNeighborHMAC));
            }
            var npAckUdpData = npAck.Encode(cfm.AtoEP);
            RespondToRequestAndRetransmissions(cfm.DecodedUdpPayloadData, npAckUdpData, remoteEndpoint);
        }

        /// <summary>
        /// protects local per from processing same (retransmitted) REQ packet
        /// protects the P2P network against looped REQ requests
        /// avoids conflicts in response scanners (e.g ACK1, ACK2 scanners)
        /// </summary>
        HashSet<RegistrationId> _pendingRegisterRequests = new HashSet<RegistrationId>();
        internal bool PendingRegisterRequestExists(RegistrationId regId) => _pendingRegisterRequests.Contains(regId);

    }
}
