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
            string messageText, Action<Exception> cb)
        {
            Engine.EngineThreadQueue.Enqueue(async () =>
            {
                Logger logger = null;
                try
                {
                    var sw = Stopwatch.StartNew();
                    var session = await SendInviteAsync(requesterUserCertificate, responderRegistrationId, responderUserId, SessionType.asyncShortSingleMessage, (logger2)=>
                    {
                        logger = logger2;
                        if (Engine.Configuration.SandboxModeOnly_EnableInsecureLogs) if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"creating an invite session to send a message '{messageText}'");
                    });
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"invite session is ready to set up direct channel and send a message");
                    try
                    {
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"remote peer accepted invite session in {(int)sw.Elapsed.TotalMilliseconds}ms: {session.RemoteSessionDescription}");

                        await session.SetupAEkeysAsync();

                        await session.SendShortSingleMessageAsync(messageText, requesterUserCertificate);
                    }
                    finally
                    {
                        session.Dispose();
                    }

                    cb?.Invoke(null);
                }
                catch (Exception exc)
                {
                    logger?.WriteToLog_mediumPain($"sending INVITE failed: {exc}");
                    cb?.Invoke(exc);
                }
            }, "BeginSendShortSingleMessage6342");
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
        public async Task<InviteSession> SendInviteAsync(UserCertificate requesterUserCertificate, RegistrationId responderRegistrationId, UserId responderUserId, SessionType sessionType, Action<Logger> loggerCb = null)
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
                var logger = new Logger(Engine, this, req, DrpPeerEngine.VisionChannelModuleName_inv_requesterSide);
                loggerCb?.Invoke(logger);
                Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                req.RequesterRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, req.GetSharedSignedFields, this.Configuration.LocalPeerRegistrationPrivateKey);

                this.TestDirection(logger, req.ResponderRegistrationId);
                var routedRequest = new RoutedRequest(logger, null, null, null, req, null);
            _retry:

                // find best connected peer to send the request
                var destinationPeer = Engine.RouteInviteRequest(this, routedRequest);
                if (destinationPeer == null) throw new NoNeighborsToSendInviteException();
                InviteAck1Packet ack1;
                try
                {
                    var reqUdpData = req.Encode_SetP2pFields(destinationPeer);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending {req}, waiting for NPACK");

                    var sentRequest = new SentRequest(Engine, logger, destinationPeer.RemoteEndpoint, destinationPeer, reqUdpData, req.ReqP2pSeq16, InviteAck1Packet.GetScanner(logger, req, destinationPeer));
                    var ack1UdpData = await sentRequest.SendRequestAsync("ack1 4146");
                                       
                    #region wait for ACK1
                    await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK("ack1 26892", reqUdpData, req.ReqP2pSeq16, req.GetSignedFieldsForNeighborHMAC);
               
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
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified ACK1");
                    session.DeriveSharedInviteAckDhSecret(Engine.CryptoLibrary, ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                    // send NPACK to ACK1
                    SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
                    #endregion
                }
                catch (RequestFailedException exc2)
                {
                    logger.WriteToLog_higherLevelDetail($"trying again on error {exc2}... alreadyTriedProxyingToDestinationPeers.Count={routedRequest.TriedNeighbors.Count}");
                    routedRequest.TriedNeighbors.Add(destinationPeer);
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

                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK2, waiting for NPACK");
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK("ack2 234575672", ack2UdpData, ack2.ReqP2pSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received NPACK");
                #endregion

                #region wait for CFM
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for CFM");
                var cfmUdpData = await Engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest("cfm 1235695", destinationPeer.RemoteEndpoint,
                                InviteConfirmationPacket.GetScanner(logger, req, destinationPeer),
                                Engine.DateTimeNowUtc, Engine.Configuration.CfmTimoutS
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

                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified CFM");

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
        
        void SendNeighborPeerAckResponseToAck1(InviteAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ack1.ReqP2pSeq16,
                ResponseCode = ResponseOrFailureCode.accepted
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
                ResponseCode = ResponseOrFailureCode.accepted
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
                ResponseCode = ResponseOrFailureCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, cfm.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(cfm.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }

        //void if (logger.WriteToLog_detail2_enabled) logger.WriteToLog_detail(string msg, object req)
        //{
        //    Engine.if (logger.WriteToLog_detail2_enabled) logger.WriteToLog_detail(msg, req, this);
        //}
    }
}
