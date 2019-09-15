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
        /// <summary>
        /// sends INVITE, autenticates users, returns Session to be used to create direct cannel
        /// </summary>
        /// <param name="responderUserId">
        /// comes from local contact book
        /// </param>
        /// <param name="responderPublicKey">
        /// comes from local contact book
        /// </param>
        public async Task<Session> SendInviteAsync(UserCertificate requesterUserCertificate, RegistrationPublicKey responderPublicKey,
            UserID_PublicKeys responderUserId)
        {
            var session = new Session(this);

            var syn = new InviteSynPacket
            {
                NumberOfHopsRemaining = 10,
                RequesterEcdhePublicKey = new EcdhPublicKey(session.LocalEcdhePublicKey),
                RequesterPublicKey = this.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                ResponderPublicKey = responderPublicKey,
                Timestamp32S = _engine.Timestamp32S,
            };
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            syn.RequesterSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, syn.GetSharedSignedFields, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);

            // find best connected peer to send the request
            var connectionToNeighbor = _engine.RouteSynInviteAtRequester(this, syn);

            var synUdpData = syn.Encode_SetP2pFields(connectionToNeighbor);

            WriteToLog_inv_requesterSide_detail($"sending SYN, waiting for NHACK");
            await connectionToNeighbor.SendUdpRequestAsync_Retransmit_WaitForNHACK(synUdpData, syn.NhaSeq16);
            WriteToLog_inv_requesterSide_detail($"received NHACK");


            #region wait for SYNACK
            WriteToLog_inv_requesterSide_detail($"waiting for SYNACK");
            var inviteSynAckPacketData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(connectionToNeighbor.RemoteEndpoint,
                            InviteSynAckPacket.GetScanner(syn, connectionToNeighbor),
                                _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                            ));
            if (inviteSynAckPacketData == null) throw new DrpTimeoutException();

            // SenderHMAC and SenderToken32 are already verified by scanner
            var synAck = InviteSynAckPacket.Decode(inviteSynAckPacketData);
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(synAck.ResponderEcdhePublicKey.Ecdh25519PublicKey);
            if (!synAck.ResponderSignature.Verify(_engine.CryptoLibrary, w =>
                {
                    syn.GetSharedSignedFields(w);
                    synAck.GetSharedSignedFields(w, true);
                },
                responderPublicKey))
                throw new BadSignatureException();
            WriteToLog_inv_requesterSide_detail($"verified SYNACK");
            session.DeriveSharedDhSecret(_engine.CryptoLibrary, synAck.ResponderEcdhePublicKey.Ecdh25519PublicKey);
            #endregion



            // decode and verify SD
            session.RemoteSessionDescription = SessionDescription.Decrypt_Verify(_engine.CryptoLibrary, 
                synAck.ToResponderSessionDescriptionEncrypted, 
                syn, synAck, false, session,                
                responderUserId, _engine.DateTimeNowUtc);

            // sign and encode local SD
            session.LocalSessionDescription = new SessionDescription
            {
                DirectChannelEndPoint = connectionToNeighbor.LocalEndpoint,
                UserCertificate = requesterUserCertificate
            };
            session.LocalSessionDescription.UserCertificateSignature = UserCertificateSignature.Sign(_engine.CryptoLibrary,
                w =>
                {
                    syn.GetSharedSignedFields(w);
                    synAck.GetSharedSignedFields(w, true);
                    session.LocalSessionDescription.WriteSignedFields(w);
                },
                requesterUserCertificate
                );

            #region send ack1
            var ack1 = new InviteAck1Packet
            {
                RequesterPublicKey = syn.RequesterPublicKey,
                ResponderPublicKey = syn.ResponderPublicKey,
                Timestamp32S = syn.Timestamp32S,
                ToRequesterSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(_engine.CryptoLibrary, syn, synAck, session, true)
            };
            ack1.RequesterSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                {
                    syn.GetSharedSignedFields(w);
                    synAck.GetSharedSignedFields(w, true);
                    ack1.GetSharedSignedFields(w);
                }, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
            var ack1UdpData = ack1.Encode_SetP2pFields(connectionToNeighbor);

            WriteToLog_inv_requesterSide_detail($"sending ACK1, waiting for NHACK");
            await connectionToNeighbor.SendUdpRequestAsync_Retransmit_WaitForNHACK(ack1UdpData, ack1.NhaSeq16);
            WriteToLog_inv_requesterSide_detail($"received NHACK");
            #endregion
            

            #region wait for ACK2
            WriteToLog_inv_requesterSide_detail($"waiting for ACK2");
            var ack2PacketData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(connectionToNeighbor.RemoteEndpoint,
                            InviteAck2Packet.GetScanner(syn, connectionToNeighbor),
                            _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                            ));
            if (ack2PacketData == null) throw new DrpTimeoutException();

            // SenderHMAC and SenderToken32 are already verified by scanner
            var ack2 = InviteAck2Packet.Decode(ack2PacketData);
         
            if (!ack2.ResponderSignature.Verify(_engine.CryptoLibrary, w =>
            {
                syn.GetSharedSignedFields(w);
                synAck.GetSharedSignedFields(w, true);
                ack2.GetSharedSignedFields(w);
            },
                responderPublicKey))
                throw new BadSignatureException();

            WriteToLog_inv_requesterSide_detail($"verified ACK2");
            #endregion

            return session;
        }



        void WriteToLog_inv_requesterSide_detail(string msg)
        {
            _engine.WriteToLog_inv_requesterSide_detail(msg);
        }
    }
}
