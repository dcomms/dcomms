using Dcomms.DRP.Packets;
using Dcomms.DMP;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {
        public void BeginSendShortSingleMessage(UserCertificate requesterUserCertificate, RegistrationId responderRegistrationId, UserId responderUserId,            
            string messageText, Action cb)
        {
            Engine.EngineThreadQueue.Enqueue(async () =>
            {
                object req = null;
                try
                {
                    var sw = Stopwatch.StartNew();
                    var session = await SendInviteAsync(requesterUserCertificate, responderRegistrationId, responderUserId, SessionType.asyncShortSingleMessage, (req2)=>
                    {
                        req = req2;
                        if (Engine.Configuration.SandboxModeOnly_EnableInsecureLogs) WriteToLog_inv_requesterSide_detail($"creating an invite session to send a message '{messageText}'", req2);
                    });
                    WriteToLog_inv_requesterSide_detail($"invite session is ready to set up direct channel and send a message", req);
                    try
                    {
                        WriteToLog_inv_requesterSide_detail($"remote peer accepted invite session in {(int)sw.Elapsed.TotalMilliseconds}ms: {session.RemoteSessionDescription}", req);

                        await session.SetupAEkeysAsync();

                        await session.SendShortSingleMessageAsync(messageText, requesterUserCertificate);
                    }
                    finally
                    {
                        session.Dispose();
                    }

                    if (cb != null) cb();
                }
                catch (Exception exc)
                {
                    Engine.HandleExceptionInInviteRequester(exc, req, this);
                }
            });
        }


        /// <summary>
        /// sends INVITE, autenticates users, returns Session to be used to create direct cannel
        /// </summary>
        /// <param name="responderUserId">
        /// comes from local contact book
        /// </param>
        /// <param name="responderRegId">
        /// comes from local contact book
        /// </param>
        public async Task<InviteSession> SendInviteAsync(UserCertificate requesterUserCertificate, RegistrationId responderRegistrationId, UserId responderUserId, SessionType sessionType, Action<InviteRequestPacket> reqCb = null)
        {
            var session = new InviteSession(this);
            try
            {
                var req = new InviteRequestPacket
                {
                    NumberOfHopsRemaining = 10,
                    RequesterEcdhePublicKey = new EcdhPublicKey(session.LocalInviteAckEcdhePublicKey),
                    RequesterRegistrationId = this.Configuration.LocalPeerRegistrationId,
                    ResponderRegistrationId = responderRegistrationId,
                    ReqTimestamp32S = Engine.Timestamp32S,
                };
                reqCb?.Invoke(req);
                Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                req.RequesterRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, req.GetSharedSignedFields, this.Configuration.LocalPeerRegistrationPrivateKey);

                var alreadyTriedProxyingToDestinationPeers = new HashSet<ConnectionToNeighbor>();
            _retry:

                // find best connected peer to send the request
                var destinationPeer = Engine.RouteInviteRequest(this, req, null, alreadyTriedProxyingToDestinationPeers);
                if (destinationPeer == null) throw new NoNeighborsToSendInviteException();
                InviteAck1Packet ack1;
                try
                {
                    var reqUdpData = req.Encode_SetP2pFields(destinationPeer);

                    WriteToLog_inv_requesterSide_detail($"sending {req}, waiting for NPACK", req);
                    await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(reqUdpData, req.ReqP2pSeq16, req.GetSignedFieldsForNeighborHMAC);
                    WriteToLog_inv_requesterSide_detail($"received NPACK", req);

                    #region wait for ACK1
                    WriteToLog_inv_requesterSide_detail($"waiting for ACK1", req);
                    var ack1UdpData = await Engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                    InviteAck1Packet.GetScanner(req, destinationPeer),
                                        Engine.DateTimeNowUtc, Engine.Configuration.InviteRequestsTimoutS
                                    ));
                    if (ack1UdpData == null) throw new DrpTimeoutException();

                    // NeighborHMAC and NeighborToken32 are already verified by scanner
                    ack1 = InviteAck1Packet.Decode(ack1UdpData);
                    Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);
                    if (!ack1.ResponderRegistrationSignature.Verify(Engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w);
                            ack1.GetSharedSignedFields(w, true);
                        },
                        responderRegistrationId))
                        throw new BadSignatureException();
                    WriteToLog_inv_requesterSide_detail($"verified ACK1", req);
                    session.DeriveSharedInviteAckDhSecret(Engine.CryptoLibrary, ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                    // send NPACK to ACK1
                    SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
                    #endregion
                }
                catch (Exception exc2)
                {
                    Engine.HandleExceptionInInviteRequester(exc2, req, this);
                    WriteToLog_inv_requesterSide_detail($"trying again on error... alreadyTriedProxyingToDestinationPeers.Count={alreadyTriedProxyingToDestinationPeers.Count}", req);
                    alreadyTriedProxyingToDestinationPeers.Add(destinationPeer);
                    goto _retry;
                }

                // decode and verify SD
                session.RemoteSessionDescription = InviteSessionDescription.Decrypt_Verify(Engine.CryptoLibrary,
                    ack1.ToResponderSessionDescriptionEncrypted,
                    req, ack1, false, session,
                    responderUserId, Engine.DateTimeNowUtc);

                // sign and encode local SD
                session.LocalSessionDescription = new InviteSessionDescription
                {
                    DirectChannelEndPoint = destinationPeer.LocalEndpoint,
                    SessionType = sessionType,
                    DirectChannelToken32 = session.LocalDirectChannelToken32
                };

                session.LocalSessionDescription.UserCertificate = requesterUserCertificate;

                session.LocalSessionDescription.UserCertificateSignature = UserCertificateSignature.Sign(Engine.CryptoLibrary,
                    w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        session.LocalSessionDescription.WriteSignedFields(w);
                    },
                    requesterUserCertificate
                    );

                #region send ack2
                var ack2 = new InviteAck2Packet
                {
                    RequesterRegistrationId = req.RequesterRegistrationId,
                    ResponderRegistrationId = req.ResponderRegistrationId,
                    ReqTimestamp32S = req.ReqTimestamp32S,
                    ToRequesterSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(Engine.CryptoLibrary, req, ack1, session, true)
                };
                ack2.RequesterRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    }, this.Configuration.LocalPeerRegistrationPrivateKey);
                var ack2UdpData = ack2.Encode_SetP2pFields(destinationPeer);

                WriteToLog_inv_requesterSide_detail($"sending ACK2, waiting for NPACK", req);
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2UdpData, ack2.ReqP2pSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                WriteToLog_inv_requesterSide_detail($"received NPACK", req);
                #endregion

                #region wait for CFM
                WriteToLog_inv_requesterSide_detail($"waiting for CFM", req);
                var cfmUdpData = await Engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                InviteConfirmationPacket.GetScanner(req, destinationPeer),
                                Engine.DateTimeNowUtc, Engine.Configuration.InviteRequestsTimoutS
                                ));
                if (cfmUdpData == null) throw new DrpTimeoutException();

                // NeighborHMAC and NeighborToken32 are already verified by scanner
                var cfm = InviteConfirmationPacket.Decode(cfmUdpData);

                if (!cfm.ResponderRegistrationSignature.Verify(Engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    },
                    responderRegistrationId))
                    throw new BadSignatureException();

                WriteToLog_inv_requesterSide_detail($"verified CFM", req);

                // send NPACK to CFM
                SendNeighborPeerAckResponseToCfm(cfm, destinationPeer);
                #endregion

                session.DeriveSharedPingPongHmacKey(req, ack1, ack2, cfm);
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        internal void SendNeighborPeerAckResponseToReq(InviteRequestPacket req, ConnectionToNeighbor neighbor, NextHopResponseOrFailureCode responseCode)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = req.ReqP2pSeq16,
                ResponseCode = responseCode
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, req.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(req.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }

        internal void SendErrorResponseToInviteReq(InviteRequestPacket req, IPEndPoint requesterEndpoint,
            ConnectionToNeighbor neighborWhoSentRequest, bool alreadyRepliedWithNPA, NextHopResponseOrFailureCode errorCode)
        {
            Engine.WriteToLog_inv_proxySide_detail($"routing failed, executing SendErrorResponseToInviteReq()", req, this);
            if (alreadyRepliedWithNPA)
            {
                // send FAILURE
                _ = RespondToSourcePeerWithAck1_Error(requesterEndpoint, req, neighborWhoSentRequest, errorCode);
            }
            else
            {
                // send NPACK
                SendNeighborPeerAckResponseToReq(req, neighborWhoSentRequest, errorCode);
            }
        }


        void SendNeighborPeerAckResponseToAck1(InviteAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ack1.ReqP2pSeq16,
                ResponseCode = NextHopResponseOrFailureCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, ack1.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(ack1.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }
        void SendNeighborPeerAckResponseToAck2(InviteAck2Packet ack2, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ack2.ReqP2pSeq16,
                ResponseCode = NextHopResponseOrFailureCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, ack2.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(ack2.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);
        }
        void SendNeighborPeerAckResponseToCfm(InviteConfirmationPacket cfm, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = cfm.ReqP2pSeq16,
                ResponseCode = NextHopResponseOrFailureCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, cfm.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(cfm.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }

        void WriteToLog_inv_requesterSide_detail(string msg, object req)
        {
            Engine.WriteToLog_inv_requesterSide_detail(msg, req, this);
        }
    }
}
