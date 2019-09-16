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
        /// Timestamp32S, SenderToken32 and SenderHMAC are verified at this time
        /// </summary>
        internal async Task AcceptInviteRequestAsync(InviteRequestPacket syn, ConnectionToNeighbor requester)
        {
            if (!syn.RequesterPublicKey.Equals(this.RegistrationConfiguration.LocalPeerRegistrationPublicKey))
                throw new ArgumentException();

            _engine.WriteToLog_inv_responderSide_detail($"accepting invite");
            
            // check if regID exists in contact book, get userID from the local contact book
            // ignore the REQ packet if no such user in contacts
            var remoteRequesterUserIdFromLocalContactBook = this._user.OnReceivedInvite_LookupUser(syn.RequesterPublicKey);
            if (remoteRequesterUserIdFromLocalContactBook == null)
            { // ignore INVITEs from unknown users
                _engine.WriteToLog_inv_responderSide_detail($"ignored invite from unknown user (no user found in local contact book by requester regID)");
                return;
            }

            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            _engine.RecentUniqueInviteRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);

            // verify requester reg. signature
            if (!syn.RequesterSignature.Verify(_engine.CryptoLibrary, syn.GetSharedSignedFields, syn.RequesterPublicKey))
                throw new BadSignatureException();
            
            _pendingInviteRequests.Add(syn.RequesterPublicKey);

            try
            {
                // send NPACK to REQ
                _engine.WriteToLog_inv_responderSide_detail($"sending NPACK to REQ requester");
                SendNextHopAckResponseToReq(syn, requester);

                var session = new Session(this);
                session.DeriveSharedDhSecret(_engine.CryptoLibrary, syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                session.LocalSessionDescription = _user.OnReceivedInvite_GetLocalSessionDescription(remoteRequesterUserIdFromLocalContactBook);

                #region send SYNACK. sign local SD by local user
                var synAck = new InviteAck1Packet
                {
                    Timestamp32S = syn.Timestamp32S,
                    RequesterPublicKey = syn.RequesterPublicKey,
                    ResponderPublicKey = syn.ResponderPublicKey,
                    ResponderEcdhePublicKey = new EcdhPublicKey(session.LocalEcdhePublicKey),                         
                };
                synAck.ToResponderSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(_engine.CryptoLibrary,
                    syn, synAck, session, false);
                synAck.ResponderSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                    {
                        syn.GetSharedSignedFields(w);
                        synAck.GetSharedSignedFields(w, true);
                    },
                    this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                );

                var synAckData = synAck.Encode_SetP2pFields(requester);
                _engine.WriteToLog_inv_responderSide_detail($"sending SYNACK to requester, awaiting for NPACK");
                _ = requester.SendUdpRequestAsync_Retransmit_WaitForNPACK(synAckData, synAck.NpaSeq16, synAck.GetSignedFieldsForSenderHMAC);
                // not waiting for NPACK, wait for ACK1
                #endregion

                // wait for ACK1
                _engine.WriteToLog_inv_responderSide_detail($"waiting for ACK1");
                var ack1UdpData = await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requester.RemoteEndpoint,
                    InviteAck2Packet.GetScanner(syn, requester));
                _engine.WriteToLog_inv_responderSide_detail($"received ACK1");
                var ack1 = InviteAck2Packet.Decode(ack1UdpData);
                if (!ack1.RequesterSignature.Verify(_engine.CryptoLibrary, w =>
                    {
                        syn.GetSharedSignedFields(w);
                        synAck.GetSharedSignedFields(w, true);
                        ack1.GetSharedSignedFields(w);
                    }, syn.RequesterPublicKey))
                    throw new BadSignatureException();
                // decrypt, verify SD remote user's certificate and signature
                session.RemoteSessionDescription = SessionDescription.Decrypt_Verify(_engine.CryptoLibrary, 
                    ack1.ToRequesterSessionDescriptionEncrypted,
                    syn, synAck, true, session, remoteRequesterUserIdFromLocalContactBook, _engine.DateTimeNowUtc);

                // send NPACK to ACK1
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to ACK1 to requester");
                SendNextHopAckResponseToAck2(ack1, requester);


                // send ACK2 with signature
                var ack2 = new InviteConfirmationPacket
                {
                    Timestamp32S = syn.Timestamp32S,
                    RequesterPublicKey = syn.RequesterPublicKey,
                    ResponderPublicKey = syn.ResponderPublicKey,                    
                };
                ack2.ResponderSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                {
                    syn.GetSharedSignedFields(w);
                    synAck.GetSharedSignedFields(w, true);
                    ack1.GetSharedSignedFields(w);
                }, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                var ack2Data = ack2.Encode_SetP2pFields(requester);

                _engine.WriteToLog_inv_responderSide_detail($"sending ACK2 to requester, waiting for NPACK");
                await requester.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2Data, ack2.NpaSeq16, ack2.GetSignedFieldsForSenderHMAC);
                _engine.WriteToLog_inv_responderSide_detail($"received NPACK to ACK2");

                _user.OnAcceptedIncomingInvite(session);
            }
            catch (Exception exc)
            {
                _engine.HandleExceptionWhileAcceptingInvite(exc);
            }
            finally
            {
                _pendingInviteRequests.Remove(syn.RequesterPublicKey);
            }
        }
    }
}
