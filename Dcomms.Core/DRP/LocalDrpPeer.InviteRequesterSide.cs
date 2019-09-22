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
                try
                {
                    var sw = Stopwatch.StartNew();
                    var session = await SendInviteAsync(requesterUserCertificate, responderRegistrationId, responderUserId, SessionType.asyncShortSingleMessage);
                    try
                    {
                        WriteToLog_inv_requesterSide_detail($"remote peer accepted invite session in {(int)sw.Elapsed.TotalMilliseconds}ms: {session.RemoteSessionDescription}");

                        await session.SetupAEkeysAsync();

                        await session.SendShortSingleMessageAsync(messageText);
                    }
                    finally
                    {
                        session.Dispose();
                    }

                    if (cb != null) cb();
                }
                catch (Exception exc)
                {
                    Engine.HandleExceptionInInviteRequester(exc);
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
        public async Task<InviteSession> SendInviteAsync(UserCertificate requesterUserCertificate, RegistrationId responderRegistrationId, UserId responderUserId, SessionType sessionType)
        {
            var session = new InviteSession(this);
            try
            {
                var req = new InviteRequestPacket
                {
                    NumberOfHopsRemaining = 10,
                    RequesterEcdhePublicKey = new EcdhPublicKey(session.LocalInviteAckEcdhePublicKey),
                    RequesterRegistrationId = this.RegistrationConfiguration.LocalPeerRegistrationId,
                    ResponderRegistrationId = responderRegistrationId,
                    ReqTimestamp32S = Engine.Timestamp32S,
                };
                Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                req.RequesterRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, req.GetSharedSignedFields, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);

                // find best connected peer to send the request
                var destinationPeer = Engine.RouteInviteRequest(this, req);

                var reqUdpData = req.Encode_SetP2pFields(destinationPeer);

                WriteToLog_inv_requesterSide_detail($"sending REQ, waiting for NPACK");
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(reqUdpData, req.NpaSeq16, req.GetSignedFieldsForNeighborHMAC);
                WriteToLog_inv_requesterSide_detail($"received NPACK");

                #region wait for ACK1
                WriteToLog_inv_requesterSide_detail($"waiting for ACK1");
                var ack1UdpData = await Engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                InviteAck1Packet.GetScanner(req, destinationPeer),
                                    Engine.DateTimeNowUtc, Engine.Configuration.InviteRequestsTimoutS
                                ));
                if (ack1UdpData == null) throw new DrpTimeoutException();

                // NeighborHMAC and NeighborToken32 are already verified by scanner
                var ack1 = InviteAck1Packet.Decode(ack1UdpData);
                Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);
                if (!ack1.ResponderRegistrationSignature.Verify(Engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                    },
                    responderRegistrationId))
                    throw new BadSignatureException();
                WriteToLog_inv_requesterSide_detail($"verified ACK1");
                session.DeriveSharedInviteAckDhSecret(Engine.CryptoLibrary, ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                // send NPACK to ACK1
                SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
                #endregion

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
                    }, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                var ack2UdpData = ack2.Encode_SetP2pFields(destinationPeer);

                WriteToLog_inv_requesterSide_detail($"sending ACK2, waiting for NPACK");
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2UdpData, ack2.NpaSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                WriteToLog_inv_requesterSide_detail($"received NPACK");
                #endregion

                #region wait for CFM
                WriteToLog_inv_requesterSide_detail($"waiting for CFM");
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

                WriteToLog_inv_requesterSide_detail($"verified CFM");

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

        void SendNeighborPeerAckResponseToReq(InviteRequestPacket req, ConnectionToNeighbor neighbor, NextHopResponseCode statusCode = NextHopResponseCode.accepted)
        {
            var npAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = req.NpaSeq16,
                StatusCode = statusCode
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, req.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(req.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }
        void SendNeighborPeerAckResponseToAck1(InviteAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            var npAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = ack1.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
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
                NpaSeq16 = ack2.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
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
                NpaSeq16 = cfm.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };

            npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
            npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, cfm.GetSignedFieldsForNeighborHMAC));
            var npAckUdpData = npAck.Encode(false);

            Engine.RespondToRequestAndRetransmissions(cfm.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }

        void WriteToLog_inv_requesterSide_detail(string msg)
        {
            Engine.WriteToLog_inv_requesterSide_detail(msg);
        }
    }
}
