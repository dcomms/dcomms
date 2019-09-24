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
        internal async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RegisterRequestPacket req, IPEndPoint requesterEndpoint, ConnectionToNeighbor sourcePeer) // engine thread
        {
            
            if (req.AtoEP ^ (sourcePeer == null))
                throw new InvalidOperationException();

            if (sourcePeer == null)
            {
                // check Timestamp32S and signature of requester (A)
                if (!ValidateReceivedReqTimestamp32S(req.ReqTimestamp32S) ||
                        !req.RequesterSignature.Verify(_cryptoLibrary,
                            w => req.GetSharedSignedFields(w, false),
                            req.RequesterRegistrationId
                        )
                    )
                    throw new BadSignatureException();
                if (req.EpEndpoint.Address.Equals(acceptAt.PublicIpApiProviderResponse) == false)
                {
                    throw new PossibleMitmException();
                }
            }

            if (_pendingRegisterRequests.Contains(req.RequesterRegistrationId))
            {
                // received duplicate REGISTER REQ packet
                WriteToLog_reg_responderSide_detail($"ignoring duplicate registration request from {requesterEndpoint}");
                return;
            }

            WriteToLog_reg_responderSide_detail($"accepting registration from {requesterEndpoint}: NpaSeq16={req.NpaSeq16}, NumberOfHopsRemaining={req.NumberOfHopsRemaining}, epEndpoint={req.EpEndpoint}, sourcePeer={sourcePeer}");

            _recentUniqueRegistrationRequests.AssertIsUnique(req.GetUniqueRequestIdFields);
            RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

            _pendingRegisterRequests.Add(req.RequesterRegistrationId);
            try
            {
                WriteToLog_reg_responderSide_detail($"sending NPACK to REQ to {requesterEndpoint}");
                SendNeighborPeerAckResponseToRegisterReq(req, requesterEndpoint, NextHopResponseCode.accepted, sourcePeer);

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer)
                {
                    LocalEndpoint = sourcePeer?.LocalEndpoint ?? req.EpEndpoint,
					RemotePeerPublicKey = req.RequesterRegistrationId					
                };
                byte[] ack1UdpData;
                try
                {
                    var ack1 = new RegisterAck1Packet
                    {                        
                        RequesterRegistrationId = req.RequesterRegistrationId,
                        ReqTimestamp32S = req.ReqTimestamp32S,
                        ResponderEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                        ResponderRegistrationId = acceptAt.RegistrationConfiguration.LocalPeerRegistrationId,
                        ResponderStatusCode = DrpResponderStatusCode.confirmed,
                        NpaSeq16 = GetNewNpaSeq16_AtoEP(),
                    };
                    RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);
                    ack1.ToResponderTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack1_ToResponderTxParametersEncrypted_AtResponder_DeriveSharedDhSecret(req, ack1, sourcePeer);
                    ack1.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        (w2) =>
                        {
                            req.GetSharedSignedFields(w2, true);
                            ack1.GetSharedSignedFields(w2, false, true);
                        },
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (sourcePeer == null) ack1.RequesterEndpoint = requesterEndpoint;                    
                    ack1UdpData = ack1.Encode_OpionallySignNeighborHMAC(sourcePeer);
                    
                    var ack2Scanner = RegisterAck2Packet.GetScanner(sourcePeer, req.RequesterRegistrationId, req.ReqTimestamp32S);
                    byte[] ack2UdpData;
                    if (sourcePeer == null)
                    {   // wait for ACK2, retransmitting ACK1
                        WriteToLog_reg_responderSide_detail($"sending ACK1, waiting for ACK2");
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(ack1UdpData, requesterEndpoint, ack2Scanner);
                    }
                    else
                    {   // retransmit ACK1 until NPACK (via P2P); at same time wait for ACK
                        WriteToLog_reg_responderSide_detail($"sending ACK1, awaiting for NPACK");
                        _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpData, requesterEndpoint,
                            ack1.NpaSeq16, sourcePeer, ack1.GetSignedFieldsForNeighborHMAC);
                        // not waiting for NPACK, wait for ACK
                        WriteToLog_reg_responderSide_detail($"waiting for ACK2");                        
                        ack2UdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, ack2Scanner);                
                    }

                    WriteToLog_reg_responderSide_detail($"received ACK2");
                    var ack2 = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(ack2UdpData, req, ack1, newConnectionToNeighbor);

                    WriteToLog_reg_responderSide_detail($"verified ACK2");
                    acceptAt.ConnectedNeighbors.Add(newConnectionToNeighbor); // added to list here in order to respond to ping requests from A                    
                    SendNeighborPeerAckResponseToRegisterAck2(ack2, requesterEndpoint, sourcePeer); // send NPACK to ACK

                    _ = WaitForRegistrationConfirmationRequestAsync(requesterEndpoint, req, newConnectionToNeighbor, sourcePeer);

                    #region send ping, verify pong
                    var ping = newConnectionToNeighbor.CreatePing(true);
                    
                    var pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, ping.PingRequestId32), DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    ping.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    WriteToLog_reg_responderSide_detail($"sent PING");
                    var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pong from A
                    if (pongPacketData == null) throw new DrpTimeoutException();
                    var pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                        pongPacketData, ping, newConnectionToNeighbor,
                        true);
                    WriteToLog_reg_responderSide_detail($"verified PONG");
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
                HandleExceptionInRegistrationResponder(requesterEndpoint, exc);
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
                var regCfmScanner = RegisterConfirmationPacket.GetScanner(sourcePeer, req.RequesterRegistrationId, req.ReqTimestamp32S);
                WriteToLog_reg_responderSide_detail($"waiting for CFM");
                var regCfmUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, regCfmScanner);
                WriteToLog_reg_responderSide_detail($"received CFM");
                var registerCfmPacket = RegisterConfirmationPacket.DecodeAndOptionallyVerify(regCfmUdpPayload, req, newConnectionToNeighbor);
                WriteToLog_reg_responderSide_detail($"verified CFM");

                SendNeighborPeerAckResponseToRegisterCfm(registerCfmPacket, requesterEndpoint, sourcePeer);
                WriteToLog_reg_responderSide_detail($"sent NPACK to CFM");
            }
			catch (Exception exc)
            {
                newConnectionToNeighbor.Dispose();
                HandleExceptionInRegistrationResponder(requesterEndpoint, exc);
            }
        }

        void SendNeighborPeerAckResponseToRegisterReq(RegisterRequestPacket req, IPEndPoint requesterEndpoint, NextHopResponseCode statusCode, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = req.NpaSeq16,
                StatusCode = statusCode
            };
            if (neighbor != null)
            {
                npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
                npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, req.GetSignedFieldsForNeighborHMAC));
            }
            var npAckUdpData = npAck.Encode(neighbor == null);
            
            RespondToRequestAndRetransmissions(req.DecodedUdpPayloadData, npAckUdpData, requesterEndpoint);

            //   WriteToLog_reg_responderSide_detail($"sent npAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(npAckUdpData)} nhaSeq={registerSynPacket.NpaSeq16}");
        }
        void SendNeighborPeerAckResponseToRegisterAck1(RegisterAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = ack1.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, ack1.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            RespondToRequestAndRetransmissions(ack1.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

            //   WriteToLog_reg_responderSide_detail($"sent npAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(npAckUdpData)} nhaSeq={registerSynPacket.NpaSeq16}");
        }
        void SendNeighborPeerAckResponseToRegisterAck2(RegisterAck2Packet ack2, IPEndPoint remoteEndpoint, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = ack2.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
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
                NpaSeq16 = cfm.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
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
        /// </summary>
        HashSet<RegistrationId> _pendingRegisterRequests = new HashSet<RegistrationId>();

    }
}
