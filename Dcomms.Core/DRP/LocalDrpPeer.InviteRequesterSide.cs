using Dcomms.DRP.Packets;
using Dcomms.DMP;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {
        public void BeginSendInvite(UserCertificate requesterUserCertificate, RegistrationId responderRegId, UserId responderUserId, SessionDescription localSessionDescription, Action<Session> cb)
        {
            WriteToLog_inv_requesterSide_detail($">> BeginSendInvite()");

            _engine.EngineThreadQueue.Enqueue(async () =>
            {
                try
                {
                    var r = await SendInviteAsync(requesterUserCertificate, responderRegId, responderUserId, localSessionDescription);
                    if (cb != null) cb(r);
                }
                catch (Exception exc)
                {
                    _engine.HandleExceptionInInviteRequester(exc);
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
        public async Task<Session> SendInviteAsync(UserCertificate requesterUserCertificate, RegistrationId responderRegId, UserId responderUserId, SessionDescription localSessionDescription)
        {
            var session = new Session(this);

            var req = new InviteRequestPacket
            {
                NumberOfHopsRemaining = 10,
                RequesterEcdhePublicKey = new EcdhPublicKey(session.LocalEcdhePublicKey),
                RequesterRegistrationId = this.RegistrationConfiguration.LocalPeerRegistrationId,
                ResponderRegistrationId = responderRegId,
                ReqTimestamp32S = _engine.Timestamp32S,
            };
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            req.RequesterRegistrationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, req.GetSharedSignedFields, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);

            // find best connected peer to send the request
            var destinationPeer = _engine.RouteInviteRequest(this, req);

            var reqUdpData = req.Encode_SetP2pFields(destinationPeer);

            WriteToLog_inv_requesterSide_detail($"sending REQ, waiting for NPACK");
            await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(reqUdpData, req.NpaSeq16, req.GetSignedFieldsForNeighborHMAC);
            WriteToLog_inv_requesterSide_detail($"received NPACK");


            #region wait for ACK1
            WriteToLog_inv_requesterSide_detail($"waiting for ACK1");
            var ack1UdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                            InviteAck1Packet.GetScanner(req, destinationPeer),
                                _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                            ));
            if (ack1UdpData == null) throw new DrpTimeoutException();

            // NeighborHMAC and NeighborToken32 are already verified by scanner
            var ack1 = InviteAck1Packet.Decode(ack1UdpData);
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);
            if (!ack1.ResponderRegistrationSignature.Verify(_engine.CryptoLibrary, w =>
                {
                    req.GetSharedSignedFields(w);
                    ack1.GetSharedSignedFields(w, true);
                },
                responderRegId))
                throw new BadSignatureException();
            WriteToLog_inv_requesterSide_detail($"verified ACK1");
            session.DeriveSharedDhSecret(_engine.CryptoLibrary, ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

            // send NPACK to ACK1
            SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
            #endregion



            // decode and verify SD
            session.RemoteSessionDescription = SessionDescription.Decrypt_Verify(_engine.CryptoLibrary, 
                ack1.ToResponderSessionDescriptionEncrypted, 
                req, ack1, false, session,                
                responderUserId, _engine.DateTimeNowUtc);

            // sign and encode local SD
            session.LocalSessionDescription = localSessionDescription;


            session.LocalSessionDescription.UserCertificate = requesterUserCertificate;
            
            session.LocalSessionDescription.UserCertificateSignature = UserCertificateSignature.Sign(_engine.CryptoLibrary,
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
                ToRequesterSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(_engine.CryptoLibrary, req, ack1, session, true)
            };
            ack2.RequesterRegistrationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
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
            var cfmUdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                            InviteConfirmationPacket.GetScanner(req, destinationPeer),
                            _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                            ));
            if (cfmUdpData == null) throw new DrpTimeoutException();

            // NeighborHMAC and NeighborToken32 are already verified by scanner
            var cfm = InviteConfirmationPacket.Decode(cfmUdpData);
         
            if (!cfm.ResponderRegistrationSignature.Verify(_engine.CryptoLibrary, w =>
                {
                    req.GetSharedSignedFields(w);
                    ack1.GetSharedSignedFields(w, true);
                    ack2.GetSharedSignedFields(w);
                },
                responderRegId))
                throw new BadSignatureException();

            WriteToLog_inv_requesterSide_detail($"verified CFM");
                       
            // send NPACK to CFM
            SendNeighborPeerAckResponseToCfm(cfm, destinationPeer);
            #endregion

            return session;
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

            _engine.RespondToRequestAndRetransmissions(req.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

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

            _engine.RespondToRequestAndRetransmissions(ack1.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

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

            _engine.RespondToRequestAndRetransmissions(ack2.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);
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

            _engine.RespondToRequestAndRetransmissions(cfm.DecodedUdpPayloadData, npAckUdpData, neighbor.RemoteEndpoint);

        }

        void WriteToLog_inv_requesterSide_detail(string msg)
        {
            _engine.WriteToLog_inv_requesterSide_detail(msg);
        }
    }
}
