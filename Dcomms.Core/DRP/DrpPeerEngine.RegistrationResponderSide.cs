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
        /// in P2P mode Timestamp32S, NeighborToken32 and NeighborHMAC are verified at this time
        /// </summary>
        /// <param name="receivedFromInP2pMode">
        /// is null in A-EP mode
        /// </param>
        internal async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RegisterRequestPacket req, IPEndPoint requesterEndpoint, ConnectionToNeighbor sourcePeer, DateTime reqReceivedTimeUtc) // engine thread
        {            
            if (req.RequesterRegistrationId.Equals(acceptAt.Configuration.LocalPeerRegistrationId)) throw new InvalidOperationException();

            if (sourcePeer == null)
            {
                // check Timestamp32S and signature of requester (A)
                if (!ValidateReceivedReqTimestamp64(req.ReqTimestamp64) ||
                        !req.RequesterSignature.Verify(_cryptoLibrary,
                            w => req.GetSharedSignedFields(w, false),
                            req.RequesterRegistrationId
                        )
                    )
                    throw new BadSignatureException();
                if (req.EpEndpoint.Address.Equals(acceptAt.PublicIpApiProviderResponse) == false)
                {
                    throw new PossibleAttackException();
                }
            }

            if (PendingRegisterRequestExists(req.RequesterRegistrationId))
            {
                // received duplicate REGISTER REQ packet
                WriteToLog_reg_responderSide_needsAttention($"ignoring duplicate registration request {req.RequesterRegistrationId} from {requesterEndpoint}", req, acceptAt);
                return;
            }

            if (!RecentUniqueAcceptedRegistrationRequests.Filter(req.GetUniqueRequestIdFields))
            {
                WriteToLog_reg_responderSide_needsAttention($"ignoring registration request {req.RequesterRegistrationId} ts={req.ReqTimestamp64} from {requesterEndpoint} with non-unique request ID fields", req, acceptAt);
                return;
            }

            WriteToLog_reg_responderSide_higherLevelDetail($"accepting registration from {requesterEndpoint}: ReqP2pSeq16={req.ReqP2pSeq16}, NumberOfHopsRemaining={req.NumberOfHopsRemaining}, epEndpoint={req.EpEndpoint}, sourcePeer={sourcePeer}, ts={req.ReqTimestamp64}", req, acceptAt);

            if (!RecentUniquePublicEcdhKeys.Filter(req.RequesterEcdhePublicKey.Ecdh25519PublicKey))
            {
                WriteToLog_reg_responderSide_needsAttention($"ignoring registration request {req.RequesterRegistrationId} from {requesterEndpoint} with non-unique RequesterEcdhePublicKey", req, acceptAt);
                return;
            }

            _pendingRegisterRequests.Add(req.RequesterRegistrationId);
            try
            {
                WriteToLog_reg_responderSide_detail($"sending NPACK to REQ to {requesterEndpoint} (delay={(int)(DateTimeNowUtc - reqReceivedTimeUtc).TotalMilliseconds}ms)", req, acceptAt);
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.accepted, sourcePeer);

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer, req.RequesterRegistrationId)
                {
                    LocalEndpoint = sourcePeer?.LocalEndpoint ?? req.EpEndpoint,
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
                        ResponderStatusCode = DrpResponderStatusCode.confirmed,
                        ReqP2pSeq16 = GetNewNpaSeq16_AtoEP(),
                    };
                    RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);
                    ack1.ToResponderTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack1_ToResponderTxParametersEncrypted_AtResponder_DeriveSharedDhSecret(req, ack1, sourcePeer);
                    ack1.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        (w2) =>
                        {
                            req.GetSharedSignedFields(w2, true);
                            ack1.GetSharedSignedFields(w2, false, true);
                        },
                        acceptAt.Configuration.LocalPeerRegistrationPrivateKey);
                    if (sourcePeer == null) ack1.RequesterEndpoint = requesterEndpoint;                    
                    ack1UdpData = ack1.Encode_OpionallySignNeighborHMAC(sourcePeer);
                    
                    var ack2Scanner = RegisterAck2Packet.GetScanner(sourcePeer, req);
                    byte[] ack2UdpData;
                    if (sourcePeer == null)
                    {   // wait for ACK2, retransmitting ACK1
                        WriteToLog_reg_responderSide_detail($"sending ACK1, waiting for ACK2", req, acceptAt);
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(ack1UdpData, requesterEndpoint, ack2Scanner);
                    }
                    else
                    {   // retransmit ACK1 until NPACK (via P2P); at same time wait for ACK
                        WriteToLog_reg_responderSide_detail($"sending ACK1, awaiting for NPACK", req, acceptAt);
                        _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpData, requesterEndpoint,
                            ack1.ReqP2pSeq16, sourcePeer, ack1.GetSignedFieldsForNeighborHMAC);
                        // not waiting for NPACK, wait for ACK
                        WriteToLog_reg_responderSide_detail($"waiting for ACK2", req, acceptAt);                        
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, ack2Scanner);                
                    }

                    WriteToLog_reg_responderSide_detail($"received ACK2", req, acceptAt);
                    var ack2 = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(ack2UdpData, req, ack1, newConnectionToNeighbor);
                    WriteToLog_reg_responderSide_detail($"verified ACK2", req, acceptAt);

                    acceptAt.AddToConnectedNeighbors(newConnectionToNeighbor, req); // added to list here in order to respond to ping requests from A    

                    SendNeighborPeerAckResponseToRegisterAck2(ack2, requesterEndpoint, sourcePeer); // send NPACK to ACK

                    _ = WaitForRegistrationConfirmationRequestAsync(requesterEndpoint, req, newConnectionToNeighbor, sourcePeer);

                    #region send ping, verify pong
                    var ping = newConnectionToNeighbor.CreatePing(true, false, acceptAt.ConnectedNeighborsBusySectorIds);
                    
                    var pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, ping.PingRequestId32), DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    ping.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    WriteToLog_reg_responderSide_detail($"sent PING", req, acceptAt);
                    var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pong from A
                    if (pongPacketData == null) throw new DrpTimeoutException();
                    var pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                        pongPacketData, ping, newConnectionToNeighbor,
                        true);
                    WriteToLog_reg_responderSide_detail($"verified PONG", req, acceptAt);
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
			catch (Exception exc)
            {
                HandleExceptionInRegistrationResponder(req, requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(req.RequesterRegistrationId);
            }
        }
        async Task WaitForRegistrationConfirmationRequestAsync(IPEndPoint requesterEndpoint, RegisterRequestPacket req, ConnectionToNeighbor newConnectionToNeighbor, ConnectionToNeighbor sourcePeer)
        {
            try
            {
                var regCfmScanner = RegisterConfirmationPacket.GetScanner(sourcePeer, req);
                WriteToLog_reg_responderSide_detail($"waiting for CFM", req, newConnectionToNeighbor.LocalDrpPeer);
                var regCfmUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, regCfmScanner, Configuration.RegisterRequestsTimoutS);
                WriteToLog_reg_responderSide_detail($"received CFM", req, newConnectionToNeighbor.LocalDrpPeer);
                var registerCfmPacket = RegisterConfirmationPacket.DecodeAndOptionallyVerify(regCfmUdpPayload, req, newConnectionToNeighbor);
                WriteToLog_reg_responderSide_detail($"verified CFM", req, newConnectionToNeighbor.LocalDrpPeer);

                SendNeighborPeerAckResponseToRegisterCfm(registerCfmPacket, requesterEndpoint, sourcePeer);
                WriteToLog_reg_responderSide_detail($"sent NPACK to CFM", req, newConnectionToNeighbor.LocalDrpPeer);
            }
			catch (Exception exc)
            {
                WriteToLog_reg_responderSide_lightPain($"disposing new connection because of CFM error", req, newConnectionToNeighbor.LocalDrpPeer);
                newConnectionToNeighbor.Dispose();
                HandleExceptionInRegistrationResponder(req, requesterEndpoint, exc);
            }
        }

        internal void SendNeighborPeerAckResponseToRegisterReq(RegisterRequestPacket req, IPEndPoint requesterEndpoint, NextHopResponseOrFailureCode responseCode, ConnectionToNeighbor neighbor)
        {
            neighbor?.AssertIsNotDisposed();
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = req.ReqP2pSeq16,
                ResponseCode = responseCode
            };
            if (neighbor != null)
            {
                npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
                npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, req.GetSignedFieldsForNeighborHMAC));
            }
            var npAckUdpData = npAck.Encode(neighbor == null);
            
            RespondToRequestAndRetransmissions(req.DecodedUdpPayloadData, npAckUdpData, requesterEndpoint);

        }
        internal void SendErrorResponseToRegisterReq(RegisterRequestPacket req, IPEndPoint requesterEndpoint, 
            ConnectionToNeighbor neighbor, bool alreadyRepliedWithNPA, DrpResponderStatusCode errorCode)
        {
            WriteToLog_routing_higherLevelDetail($"routing failed, executing SendErrorResponseToRegisterReq()", req, neighbor?.LocalDrpPeer);
            if (alreadyRepliedWithNPA)
            {
                // send ack1
                _ = RespondToSourcePeerWithAck1_Error(requesterEndpoint, req, neighbor, errorCode);
            }
            else
            {
                // send NPACK
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseOrFailureCode.failure_routeIsUnavailable, neighbor);
            }
        }

        internal void SendNeighborPeerAckResponseToRegisterAck1(RegisterAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ack1.ReqP2pSeq16,
                ResponseCode = NextHopResponseOrFailureCode.accepted
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
                ResponseCode = NextHopResponseOrFailureCode.accepted
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
                ResponseCode = NextHopResponseOrFailureCode.accepted
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
