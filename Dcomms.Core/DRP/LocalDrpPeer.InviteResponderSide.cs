using Dcomms.DMP;
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
        /// Timestamp32S, NeighborToken32 and NeighborHMAC are verified at this time
        /// </summary>
        internal async Task AcceptInviteRequestAsync(InviteRequestPacket req, ConnectionToNeighbor sourcePeer)
        {
            if (!req.ResponderRegistrationId.Equals(this.RegistrationConfiguration.LocalPeerRegistrationId))
                throw new ArgumentException();

            _engine.WriteToLog_inv_responderSide_detail($"accepting invite from {req.RequesterRegistrationId}");
            
            // check if regID exists in contact book, get userID from the local contact book
            // ignore the REQ packet if no such user in contacts
            var remoteRequesterUserIdFromLocalContactBook = this._drpPeerApp.OnReceivedInvite_LookupUser(req.RequesterRegistrationId);
            if (remoteRequesterUserIdFromLocalContactBook == null)
            { // ignore INVITEs from unknown users
                _engine.WriteToLog_inv_responderSide_detail($"ignored invite from unknown user (no user found in local contact book by requester regID)");
                return;
            }
            _engine.WriteToLog_inv_responderSide_detail($"resolved user {remoteRequesterUserIdFromLocalContactBook} by requester regID={req.RequesterRegistrationId}");

            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            _engine.RecentUniqueInviteRequests.AssertIsUnique(req.GetUniqueRequestIdFields);

            // verify requester reg. signature
            if (!req.RequesterRegistrationSignature.Verify(_engine.CryptoLibrary, req.GetSharedSignedFields, req.RequesterRegistrationId))
                throw new BadSignatureException();
            
            _pendingInviteRequests.Add(req.RequesterRegistrationId);

            try
            {
                // send NPACK to REQ
                _engine.WriteToLog_inv_responderSide_detail($"sending NPACK to REQ source peer");
                SendNeighborPeerAckResponseToReq(req, sourcePeer);

                var session = new InviteSession(this);
                session.DeriveSharedDhSecret(_engine.CryptoLibrary, req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                session.LocalSessionDescription = _drpPeerApp.OnReceivedInvite_GetLocalSessionDescription(remoteRequesterUserIdFromLocalContactBook, out var localUserCertificateWithPrivateKey);
                session.LocalSessionDescription.UserCertificate = localUserCertificateWithPrivateKey;
               

                #region send ACK1. sign local SD by local user
                var ack1 = new InviteAck1Packet
                {
                    ReqTimestamp32S = req.ReqTimestamp32S,
                    RequesterRegistrationId = req.RequesterRegistrationId,
                    ResponderRegistrationId = req.ResponderRegistrationId,
                    ResponderEcdhePublicKey = new EcdhPublicKey(session.LocalEcdhePublicKey),                         
                };
                session.LocalSessionDescription.UserCertificateSignature = DMP.UserCertificateSignature.Sign(_engine.CryptoLibrary, 
                    w =>
                    {
                      
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, false);
                        session.LocalSessionDescription.WriteSignedFields(w);                       
                    },
                    localUserCertificateWithPrivateKey);

                ack1.ToResponderSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(_engine.CryptoLibrary,
                    req, ack1, session, false);
                ack1.ResponderRegistrationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                    },
                    this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                );

                var ack1UdpData = ack1.Encode_SetP2pFields(sourcePeer);
                _engine.WriteToLog_inv_responderSide_detail($"sending ACK1 to source peer, awaiting for NPACK");
                _ = sourcePeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack1UdpData, ack1.NpaSeq16, ack1.GetSignedFieldsForNeighborHMAC);
                // not waiting for NPACK, wait for ACK2
                #endregion

                // wait for ACK2
                _engine.WriteToLog_inv_responderSide_detail($"waiting for ACK2");
                var ack2UdpData = await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, sourcePeer.RemoteEndpoint,
                    InviteAck2Packet.GetScanner(req, sourcePeer));
                _engine.WriteToLog_inv_responderSide_detail($"received ACK2");
                var ack2 = InviteAck2Packet.Decode(ack2UdpData);
                if (!ack2.RequesterRegistrationSignature.Verify(_engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    }, req.RequesterRegistrationId))
                    throw new BadSignatureException();
                // decrypt, verify SD remote user's certificate and signature
                session.RemoteSessionDescription = InviteSessionDescription.Decrypt_Verify(_engine.CryptoLibrary, 
                    ack2.ToRequesterSessionDescriptionEncrypted,
                    req, ack1, true, session, remoteRequesterUserIdFromLocalContactBook, _engine.DateTimeNowUtc);

                // send NPACK to ACK1
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to ACK1 to source peer");
                SendNeighborPeerAckResponseToAck2(ack2, sourcePeer);


                // send CFM with signature
                var cfm = new InviteConfirmationPacket
                {
                    ReqTimestamp32S = req.ReqTimestamp32S,
                    RequesterRegistrationId = req.RequesterRegistrationId,
                    ResponderRegistrationId = req.ResponderRegistrationId,                    
                };
                cfm.ResponderRegistrationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w);
                        ack1.GetSharedSignedFields(w, true);
                        ack2.GetSharedSignedFields(w);
                    }, this.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                var cfmUdpData = cfm.Encode_SetP2pFields(sourcePeer);

                _engine.WriteToLog_inv_responderSide_detail($"sending CFM to source peer, waiting for NPACK");
                await sourcePeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfmUdpData, cfm.NpaSeq16, cfm.GetSignedFieldsForNeighborHMAC);
                _engine.WriteToLog_inv_responderSide_detail($"received NPACK to CFM");

                _drpPeerApp.OnAcceptedIncomingInvite(session);
            }
            catch (Exception exc)
            {
                _engine.HandleExceptionWhileAcceptingInvite(exc);
            }
            finally
            {
                _pendingInviteRequests.Remove(req.RequesterRegistrationId);
            }
        }
    }
}
