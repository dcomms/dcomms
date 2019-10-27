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
            if (!req.ResponderRegistrationId.Equals(this.Configuration.LocalPeerRegistrationId))
                throw new ArgumentException();

            Engine.WriteToLog_inv_responderSide_detail($"accepting {req} from sourcePeer={sourcePeer}", req, this);
            
            // check if regID exists in contact book, get userID from the local contact book
            // ignore the REQ packet if no such user in contacts
             this._drpPeerApp.OnReceivedInvite(req.RequesterRegistrationId, out var remoteRequesterUserIdFromLocalContactBook, out var localUserCertificateWithPrivateKey, out var autoReceiveShortSingleMessage);
            if (remoteRequesterUserIdFromLocalContactBook == null)
            { // ignore INVITEs from unknown users
                Engine.WriteToLog_inv_responderSide_lightPain($"ignored invite from unknown user (no user found in local contact book by requester regID)", req, this);
                return;
            }

            if (autoReceiveShortSingleMessage == false)
            {
                Engine.WriteToLog_inv_responderSide_detail($"ignored invite: autoReceiveShortSingleMessage = false, other session types are not implemented", req, this);
                return;
            }


            Engine.WriteToLog_inv_responderSide_detail($"resolved user {remoteRequesterUserIdFromLocalContactBook} by requester regID={req.RequesterRegistrationId}", req, this);

            Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            Engine.RecentUniqueInviteRequests.AssertIsUnique(req.GetUniqueRequestIdFields);

            // verify requester reg. signature
            if (!req.RequesterRegistrationSignature.Verify(Engine.CryptoLibrary, req.GetSharedSignedFields, req.RequesterRegistrationId))
                throw new BadSignatureException();
            
            _pendingInviteRequests.Add(req.RequesterRegistrationId);

            try
            {
                // send NPACK to REQ
                Engine.WriteToLog_inv_responderSide_detail($"sending NPACK to REQ source peer", req, this);
                SendNeighborPeerAckResponseToReq(req, sourcePeer);

                var session = new InviteSession(this) { Req = req };
                try
                {
                    session.DeriveSharedInviteAckDhSecret(Engine.CryptoLibrary, req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

                    session.LocalSessionDescription = new InviteSessionDescription
                    {
                        SessionType = SessionType.asyncShortSingleMessage,
                        DirectChannelEndPoint = sourcePeer.LocalEndpoint,
                        DirectChannelToken32 = session.LocalDirectChannelToken32
                    };
                    Engine.WriteToLog_inv_responderSide_detail($"responding with local session {session.LocalSessionDescription}", req, this);

                    session.LocalSessionDescription.UserCertificate = localUserCertificateWithPrivateKey;

                    #region send ACK1. sign local SD by local user
                    var ack1 = new InviteAck1Packet
                    {
                        ReqTimestamp32S = req.ReqTimestamp32S,
                        RequesterRegistrationId = req.RequesterRegistrationId,
                        ResponderRegistrationId = req.ResponderRegistrationId,
                        ResponderEcdhePublicKey = new EcdhPublicKey(session.LocalInviteAckEcdhePublicKey),
                    };
                    session.LocalSessionDescription.UserCertificateSignature = DMP.UserCertificateSignature.Sign(Engine.CryptoLibrary,
                        w =>
                        {

                            req.GetSharedSignedFields(w);
                            ack1.GetSharedSignedFields(w, false);
                            session.LocalSessionDescription.WriteSignedFields(w);
                        },
                        localUserCertificateWithPrivateKey);

                    ack1.ToResponderSessionDescriptionEncrypted = session.LocalSessionDescription.Encrypt(Engine.CryptoLibrary,
                        req, ack1, session, false);
                    ack1.ResponderRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w);
                            ack1.GetSharedSignedFields(w, true);
                        },
                        this.Configuration.LocalPeerRegistrationPrivateKey
                    );

                    var ack1UdpData = ack1.Encode_SetP2pFields(sourcePeer);
                    Engine.WriteToLog_inv_responderSide_detail($"sending ACK1 to source peer, awaiting for NPACK", req, this);
                    _ = sourcePeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack1UdpData, ack1.NpaSeq16, ack1.GetSignedFieldsForNeighborHMAC);
                    // not waiting for NPACK, wait for ACK2
                    #endregion

                    // wait for ACK2
                    Engine.WriteToLog_inv_responderSide_detail($"waiting for ACK2", req, this);
                    var ack2UdpData = await Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, sourcePeer.RemoteEndpoint,
                        InviteAck2Packet.GetScanner(req, sourcePeer));
                    Engine.WriteToLog_inv_responderSide_detail($"received ACK2", req, this);
                    var ack2 = InviteAck2Packet.Decode(ack2UdpData);
                    if (!ack2.RequesterRegistrationSignature.Verify(Engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w);
                            ack1.GetSharedSignedFields(w, true);
                            ack2.GetSharedSignedFields(w);
                        }, req.RequesterRegistrationId))
                        throw new BadSignatureException();
                    // decrypt, verify SD remote user's certificate and signature
                    session.RemoteSessionDescription = InviteSessionDescription.Decrypt_Verify(Engine.CryptoLibrary,
                        ack2.ToRequesterSessionDescriptionEncrypted,
                        req, ack1, true, session, remoteRequesterUserIdFromLocalContactBook, Engine.DateTimeNowUtc);


                    if (session.RemoteSessionDescription.SessionType != SessionType.asyncShortSingleMessage)
                        throw new NotImplementedException();


                    // send NPACK to ACK1
                    Engine.WriteToLog_inv_proxySide_detail($"sending NPACK to ACK1 to source peer", req, this);
                    SendNeighborPeerAckResponseToAck2(ack2, sourcePeer);
                    
                    // send CFM with signature
                    var cfm = new InviteConfirmationPacket
                    {
                        ReqTimestamp32S = req.ReqTimestamp32S,
                        RequesterRegistrationId = req.RequesterRegistrationId,
                        ResponderRegistrationId = req.ResponderRegistrationId,
                    };
                    cfm.ResponderRegistrationSignature = RegistrationSignature.Sign(Engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w);
                            ack1.GetSharedSignedFields(w, true);
                            ack2.GetSharedSignedFields(w);
                        }, this.Configuration.LocalPeerRegistrationPrivateKey);
                    var cfmUdpData = cfm.Encode_SetP2pFields(sourcePeer);

                    Engine.WriteToLog_inv_responderSide_detail($"sending CFM to source peer, waiting for NPACK", req, this);
                    await sourcePeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(cfmUdpData, cfm.NpaSeq16, cfm.GetSignedFieldsForNeighborHMAC);
                    Engine.WriteToLog_inv_responderSide_detail($"received NPACK to CFM", req, this);

                    session.DeriveSharedPingPongHmacKey(req, ack1, ack2, cfm);

                    await session.SetupAEkeysAsync();
                }
                catch
                {
                    session.Dispose();
                    throw;
                }


                if (autoReceiveShortSingleMessage == true)
                {
                    _ = ReceiveShortSingleMessageAsync(session);
                }
                else
                    session.Dispose(); // todo implement other things
            }
            catch (Exception exc)
            {
                Engine.HandleExceptionWhileAcceptingInvite(exc, req, this);
            }
            finally
            {
                _pendingInviteRequests.Remove(req.RequesterRegistrationId);
            }
        }
                     

        async Task ReceiveShortSingleMessageAsync(InviteSession session)
        {
            string receivedMessage;
            try
            {
                receivedMessage = await session.ReceiveShortSingleMessageAsync(session.RemoteSessionDescription.UserCertificate); 
            }
            finally
            {
                session.Dispose();
            }

            // call app
            _drpPeerApp.OnReceivedShortSingleMessage(receivedMessage);
        }
    }
}
