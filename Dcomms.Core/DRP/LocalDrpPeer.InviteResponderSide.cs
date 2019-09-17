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
        internal async Task AcceptInviteRequestAsync(InviteRequestPacket req, ConnectionToNeighbor sourcePeer)
        {
            if (!req.RequesterPublicKey.Equals(this.RegistrationConfiguration.LocalPeerRegistrationPublicKey))
                throw new ArgumentException();

            _engine.WriteToLog_inv_responderSide_detail($"accepting invite");
            
            // check if regID exists in contact book, get userID from the local contact book
            // ignore the REQ packet if no such user in contacts
            var remoteRequesterUserIdFromLocalContactBook = this._drpPeerApp.OnReceivedInvite_LookupUser(req.RequesterPublicKey);
            if (remoteRequesterUserIdFromLocalContactBook == null)
            { // ignore INVITEs from unknown users
                _engine.WriteToLog_inv_responderSide_detail($"ignored invite from unknown user (no user found in local contact book by requester regID)");
                return;
            }

            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            _engine.RecentUniqueInviteRequests.AssertIsUnique(req.GetUniqueRequestIdFields);

            // verify requester reg. signature
            if (!req.RequesterSignature.Verify(_engine.CryptoLibrary, req.GetSharedSignedFields, req.RequesterPublicKey))
                throw new BadSignatureException();
            
            _pendingInviteRequests.Add(req.RequesterPublicKey);

            try
            {
                // send NPACK to REQ
                _engine.WriteToLog_inv_responderSide_detail($"sending NPACK to REQ source peer");
                SendNeighborPeerAckResponseToReq(req, sourcePeer);

                var session = new Session(this);
                session.DeriveSharedDhSecret(_engine.CryptoLibrary, req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                session.LocalSessionDescription = _drpPeerApp.OnReceivedInvite_GetLocalSessionDescription(remoteRequesterUserIdFromLocalContactBook);

                #region send ACK1. sign local SD by local user
                var ack1 = new InviteAck1Packet
                {
                    Timestamp32S = req.Timestamp32S,
                    RequesterPublicKey = req.RequesterPublicKey,
                    ResponderPublicKey = req.ResponderPublicKey,
                    ResponderEcdhePublicKey = new EcdhPublicKey(session.LocalEcdhePublicKey),                         
                };
                ack1.ToResponderSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(_engine.CryptoLibrary,
                    req, ack1, session, false);
                ack1.ResponderSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                    },
                    this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                );

                var ack1UdpData = ack1.Encode_SetP2pFields(sourcePeer);
                _engine.WriteToLog_inv_responderSide_detail($"sending ACK1 to source peer, awaiting for NPACK");
                _ = sourcePeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack1UdpData, ack1.NpaSeq16, ack1.GetSignedFieldsForSenderHMAC);
                // not waiting for NPACK, wait for ACK2
                #endregion

                // wait for ACK2
                _engine.WriteToLog_inv_responderSide_detail($"waiting for ACK2");
                var ack2UdpData = await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, sourcePeer.RemoteEndpoint,
                    InviteAck2Packet.GetScanner(req, sourcePeer));
                _engine.WriteToLog_inv_responderSide_detail($"received ACK2");
                var ack2 = InviteAck2Packet.Decode(ack2UdpData);
                if (!ack2.RequesterSignature.Verify(_engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    }, req.RequesterPublicKey))
                    throw new BadSignatureException();
                // decrypt, verify SD remote user's certificate and signature
                session.RemoteSessionDescription = SessionDescription.Decrypt_Verify(_engine.CryptoLibrary, 
                    ack2.ToRequesterSessionDescriptionEncrypted,
                    req, ack1, true, session, remoteRequesterUserIdFromLocalContactBook, _engine.DateTimeNowUtc);

                // send NPACK to ACK1
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to ACK1 to source peer");
                SendNeighborPeerAckResponseToAck2(ack2, sourcePeer);


                // send CFM with signature
                var cfm = new InviteConfirmationPacket
                {
                    Timestamp32S = req.Timestamp32S,
                    RequesterPublicKey = req.RequesterPublicKey,
                    ResponderPublicKey = req.ResponderPublicKey,                    
                };
                cfm.ResponderSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    }, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                var cfmUdpData = cfm.Encode_SetP2pFields(sourcePeer);

                _engine.WriteToLog_inv_responderSide_detail($"sending CFM to source peer, waiting for NPACK");
                await sourcePeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfmUdpData, cfm.NpaSeq16, cfm.GetSignedFieldsForSenderHMAC);
                _engine.WriteToLog_inv_responderSide_detail($"received NPACK to CFM");

                _drpPeerApp.OnAcceptedIncomingInvite(session);
            }
            catch (Exception exc)
            {
                _engine.HandleExceptionWhileAcceptingInvite(exc);
            }
            finally
            {
                _pendingInviteRequests.Remove(req.RequesterPublicKey);
            }
        }
    }
}
