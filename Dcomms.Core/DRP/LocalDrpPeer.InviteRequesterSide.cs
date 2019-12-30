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
            string messageText, TimeSpan? retryOnFailureUntilThisTimeout, Action<Exception> cb)
        {
            Engine.EngineThreadQueue.Enqueue(async () =>
            {
                var sw1 = Stopwatch.StartNew();

_retry:
                Logger logger = null;
                try
                {
                    var sw2 = Stopwatch.StartNew();
                    var session = await SendInviteAsync(requesterUserCertificate, responderRegistrationId, responderUserId, SessionType.asyncShortSingleMessage, null, (logger2)=>
                    {
                        logger = logger2;
                        if (Engine.Configuration.SandboxModeOnly_EnableInsecureLogs) if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"creating an invite session to send a message '{messageText}'");
                    });
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"invite session is ready to set up direct channel and send a message");
                    try
                    {
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"remote peer accepted invite session in {(int)sw2.Elapsed.TotalMilliseconds}ms: {session.RemoteSessionDescription}");

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
                    var tryAgain = retryOnFailureUntilThisTimeout.HasValue && sw1.Elapsed < retryOnFailureUntilThisTimeout.Value;
                    logger?.WriteToLog_mediumPain($"sending INVITE failed (tryAgain={tryAgain}): {exc}");
                    logger?.WriteToLog_mediumPain_EmitListOfPeers($"sending INVITE failed (tryAgain={tryAgain}): {exc}");
                    if (tryAgain)
                    {
                        logger?.WriteToLog_higherLevelDetail($"trying again to send message: sw1={sw1.Elapsed.TotalSeconds}s < retryOnFailureUntilThisTimeout={retryOnFailureUntilThisTimeout.Value.TotalSeconds}s");
                        goto _retry;
                    }
                    cb?.Invoke(exc);
                }
            }, "BeginSendShortSingleMessage6342");
        }

        public void BeginSendContactInvitation(UserCertificate requesterUserCertificate, UserId localUserId, RegistrationId[] localRegistrationIds, UserApp.ContactInvitation remotelyInitiatedInvitation, 
            TimeSpan? retryOnFailureUntilThisTimeout, Action<Exception, UserId, RegistrationId[], IPEndPoint> cb)
        {
            Engine.EngineThreadQueue.Enqueue(async () =>
            {
                var sw1 = Stopwatch.StartNew();

            _retry:
                Logger logger = null;
                try
                {
                    var sw2 = Stopwatch.StartNew();
                    var session = await SendInviteAsync(requesterUserCertificate, remotelyInitiatedInvitation.InvitationInitiatorRegistrationId, null, 
                        SessionType.contactInvitation, remotelyInitiatedInvitation.ContactInvitationToken, (logger2) =>
                    {
                        logger = logger2;
                        if (Engine.Configuration.SandboxModeOnly_EnableInsecureLogs) if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"creating an invite session to send contact invitation request");
                    });
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"invite session is ready to set up direct channel and send contact invitation request");
                    try
                    {
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"remote peer accepted invite session in {(int)sw2.Elapsed.TotalMilliseconds}ms: {session.RemoteSessionDescription}");

                        await session.SetupAEkeysAsync();

                        var (remoteUserId, remoteRegistrationIds) = await session.ExchangeContactInvitationsAsync_AtInviteRequester(requesterUserCertificate, localUserId, localRegistrationIds, session.RemoteSessionDescription.UserCertificate);
                        session.RemoteSessionDescription.UserCertificate.AssertIsValidNow(Engine.CryptoLibrary, remoteUserId, Engine.DateTimeNowUtc);
                        cb?.Invoke(null, remoteUserId, remoteRegistrationIds, session.RemoteSessionDescription.DirectChannelEndPoint);
                    }
                    finally
                    {
                        session.Dispose();
                    }
                }
                catch (Exception exc)
                {
                    var tryAgain = retryOnFailureUntilThisTimeout.HasValue && sw1.Elapsed < retryOnFailureUntilThisTimeout.Value;
                    logger?.WriteToLog_mediumPain($"sending INVITE failed (tryAgain={tryAgain}): {exc}");
                    logger?.WriteToLog_mediumPain_EmitListOfPeers($"sending INVITE failed (tryAgain={tryAgain}): {exc}");
                    if (tryAgain)
                    {
                        logger?.WriteToLog_higherLevelDetail($"trying again to send message: sw1={sw1.Elapsed.TotalSeconds}s < retryOnFailureUntilThisTimeout={retryOnFailureUntilThisTimeout.Value.TotalSeconds}s");
                        goto _retry;
                    }
                    cb?.Invoke(exc, null, null, null);
                }
            }, "BeginSendContactInvitation4534");
        }

        /// <summary>
        /// sends INVITE, autenticates users, returns Session to be used to create direct cannel
        /// </summary>
        /// <param name="responderUserId">
        /// comes from local contact book
        /// is null when sending contact invitation request
        /// </param>
        /// <param name="responderRegId">
        /// comes from local contact book
        /// </param>
        /// <param name="loggerCb">may be invoked more than one time (in case of retrying)</param>
        public async Task<InviteSession> SendInviteAsync(UserCertificate requesterUserCertificate, RegistrationId responderRegistrationId, UserId responderUserIdNullable,
            SessionType sessionType, byte[] contactInvitationTokenNullable, Action<Logger> loggerCb = null)
        {
            InviteSession session = null;
            try
            {
                var sw = Stopwatch.StartNew();
                RoutedRequest routedRequest = null;
                int trialsCount = 0;
                Exception latestTriedNeighborException = null;


 _retry:
                trialsCount++;
                session = new InviteSession(this);
                var req = new InviteRequestPacket
                {
                    NumberOfHopsRemaining = InviteRequestPacket.MaxNumberOfHopsRemaining,
                    RequesterEcdhePublicKey = new EcdhPublicKey(session.LocalInviteAckEcdhePublicKey),
                    RequesterRegistrationId = this.Configuration.LocalPeerRegistrationId,
                    RequesterNatBehaviour = Engine.LocalNatBehaviour,
                    ResponderRegistrationId = responderRegistrationId,
                    ReqTimestamp32S = Engine.Timestamp32S,
                    ContactInvitationTokenNullable = contactInvitationTokenNullable
                };
                var logger = new Logger(Engine, this, req, DrpPeerEngine.VisionChannelModuleName_inv_requesterSide);
                if (!Engine.RecentUniqueInviteRequests.Filter(req.GetUniqueRequestIdFields))
                {
                    if (trialsCount > 50) throw new NonUniquePacketFieldsException($"could not find unique fields to send INVITE request");
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting a second to generate ne wunique INVITE request");
                    await Engine.EngineThreadQueue.WaitAsync(TimeSpan.FromSeconds(1), "inv_wait_1236");
                    goto _retry;
                }


                session.Logger = logger;
                loggerCb?.Invoke(logger);
                Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey, "req.RequesterEcdhePublicKey");
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"generated unique ECDH key {req.RequesterEcdhePublicKey}");                
                req.RequesterRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, req.GetSharedSignedFields, this.Configuration.LocalPeerRegistrationPrivateKey);

                this.TestDirection(logger, req.ResponderRegistrationId);
                routedRequest = new RoutedRequest(logger, null, null, null, req, null, routedRequest);
             
                // find best connected peer to send the request
                var destinationPeer = Engine.RouteInviteRequest(this, routedRequest);
                if (destinationPeer == null)
                {
                    if (latestTriedNeighborException == null) throw new NoNeighborsToSendInviteException();
                    else throw latestTriedNeighborException;
                }
                InviteAck1Packet ack1;
                try
                {
                    var reqUdpData = req.Encode_SetP2pFields(destinationPeer);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending {req} (ReqTimestamp32S={MiscProcedures.Uint32secondsToDateTime(req.ReqTimestamp32S)}), waiting for NPACK");

                    var sentRequest = new SentRequest(Engine, logger, destinationPeer.RemoteEndpoint, destinationPeer, reqUdpData, req.ReqP2pSeq16, InviteAck1Packet.GetScanner(logger, req, destinationPeer));
                    var ack1UdpData = await sentRequest.SendRequestAsync("ack1 4146");
                                       
                    #region process ACK1
                    // NeighborHMAC and NeighborToken32 are already verified by scanner
                    ack1 = InviteAck1Packet.Decode(ack1UdpData);
                    Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey, $"ack1.ResponderEcdhePublicKey");
                    if (!ack1.ResponderRegistrationSignature.Verify(Engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w);
                            ack1.GetSharedSignedFields(w, true);
                        },
                        responderRegistrationId))
                        throw new BadSignatureException("invalid REGISTER ACK1 ResponderRegistrationSignature 2349");
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified ACK1");
                    session.DeriveSharedInviteAckDhSecret(Engine.CryptoLibrary, ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                    // send NPACK to ACK1
                    SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
                    #endregion
                }
                catch (RequestFailedException exc2)
                {
                    latestTriedNeighborException = exc2;
                    if (trialsCount > 50) throw;
                    logger.WriteToLog_higherLevelDetail($"trying again on error {exc2.Message}... alreadyTriedProxyingToDestinationPeers.Count={routedRequest.TriedNeighbors.Count}");
                    routedRequest.TriedNeighbors.Add(destinationPeer);
                    goto _retry;
                }

                // decode and verify SD
                session.RemoteSessionDescription = InviteSessionDescription.Decrypt_Verify(Engine.CryptoLibrary,
                    ack1.ToResponderSessionDescriptionEncrypted,
                    req, ack1, false, session,
                    responderUserIdNullable, Engine.DateTimeNowUtc);

                // sign and encode local SD
                session.LocalSessionDescription = new InviteSessionDescription
                {
                    DirectChannelEndPoint = destinationPeer.LocalEndpoint,
                    NatBehaviour = Engine.LocalNatBehaviour,
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
                if (cfmUdpData == null) throw new DrpTimeoutException($"did not get CFM at invite requester from destination peer {destinationPeer} (timeout={Engine.Configuration.CfmTimoutS}s)");

                // NeighborHMAC and NeighborToken32 are already verified by scanner
                var cfm = InviteConfirmationPacket.Decode(cfmUdpData);

                if (!cfm.ResponderRegistrationSignature.Verify(Engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    },
                    responderRegistrationId))
                    throw new BadSignatureException("invalid REGISTER CFM ResponderRegistrationSignature 6398");

                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified CFM");

                // send NPACK to CFM
                SendNeighborPeerAckResponseToCfm(cfm, destinationPeer);
                #endregion

                session.DeriveSharedPingPongHmacKey(req, ack1, ack2, cfm);
                return session;
            }
            catch
            {
                session?.Dispose();
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
