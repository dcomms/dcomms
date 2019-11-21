﻿using Dcomms.DMP;
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
        internal async Task AcceptInviteRequestAsync(RoutedRequest routedRequest)
        {
            if (routedRequest.ReceivedFromNeighborNullable == null) throw new ArgumentException();
            var req = routedRequest.InviteReq;
            if (!req.ResponderRegistrationId.Equals(this.Configuration.LocalPeerRegistrationId))
                throw new ArgumentException();
            var logger = routedRequest.Logger;
            logger.ModuleName = DrpPeerEngine.VisionChannelModuleName_inv_responderSide;

            if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"accepting {req} from sourcePeer={routedRequest.ReceivedFromNeighborNullable}");
            
            // check if regID exists in contact book, get userID from the local contact book
            // ignore the REQ packet if no such user in contacts
            this._drpPeerApp.OnReceivedInvite(req.RequesterRegistrationId, out var remoteRequesterUserIdFromLocalContactBook, out var localUserCertificateWithPrivateKey, out var autoReceiveShortSingleMessage);
            if (remoteRequesterUserIdFromLocalContactBook == null)
            { // ignore INVITEs from unknown users
                logger.WriteToLog_lightPain($"ignored invite from unknown user (no user found in local contact book by requester regID)");
                return;
            }

            if (autoReceiveShortSingleMessage == false)
            {
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"ignored invite: autoReceiveShortSingleMessage = false, other session types are not implemented");
                return;
            }


            if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"resolved user {remoteRequesterUserIdFromLocalContactBook} by requester regID={req.RequesterRegistrationId}");

            if (!Engine.RecentUniquePublicEcdhKeys.Filter(req.RequesterEcdhePublicKey.Ecdh25519PublicKey))
            {
                logger.WriteToLog_mediumPain($"RequesterEcdhePublicKey {req.RequesterEcdhePublicKey} is not unique, it has been recently processed");
                return;
            }
            if (!Engine.RecentUniqueInviteRequests.Filter(req.GetUniqueRequestIdFields)) 
            {
                logger.WriteToLog_mediumPain($"{req} fields are not unique, the request has been recently processed");
                return;
            }

            // verify requester reg. signature
            if (!req.RequesterRegistrationSignature.Verify(Engine.CryptoLibrary, req.GetSharedSignedFields, req.RequesterRegistrationId))
                throw new BadSignatureException();
            
            _pendingInviteRequests.Add(req.RequesterRegistrationId);

            try
            {
                // send NPACK to REQ
                if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to REQ source peer");
                routedRequest.SendNeighborPeerAck_accepted_IfNotAlreadyReplied();

                var session = new InviteSession(this);
                try
                {
                    session.DeriveSharedInviteAckDhSecret(Engine.CryptoLibrary, req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

                    session.LocalSessionDescription = new InviteSessionDescription
                    {
                        SessionType = SessionType.asyncShortSingleMessage,
                        DirectChannelEndPoint = routedRequest.ReceivedFromNeighborNullable.LocalEndpoint,
                        DirectChannelToken32 = session.LocalDirectChannelToken32
                    };
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"responding with local session {session.LocalSessionDescription}");

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

                    var ack1UdpData = ack1.Encode_SetP2pFields(routedRequest.ReceivedFromNeighborNullable);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK1 to source peer, awaiting for NPACK");
                    _ = routedRequest.ReceivedFromNeighborNullable.SendUdpRequestAsync_Retransmit_WaitForNPACK("ack1 1450", ack1UdpData, ack1.ReqP2pSeq16, ack1.GetSignedFieldsForNeighborHMAC);
                    // not waiting for NPACK, wait for ACK2
                    #endregion

                    // wait for ACK2
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"waiting for ACK2");
                    var ack2UdpData = await Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("ack2 23467789", routedRequest.ReceivedFromNeighborNullable.ToString(), null, routedRequest.ReceivedFromNeighborNullable.RemoteEndpoint,
                        InviteAck2Packet.GetScanner(logger, req, routedRequest.ReceivedFromNeighborNullable));
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received ACK2");
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


                    // send NPACK to ACK2
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending NPACK to ACK2 to source peer");
                    SendNeighborPeerAckResponseToAck2(ack2, routedRequest.ReceivedFromNeighborNullable);
                    
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
                    var cfmUdpData = cfm.Encode_SetP2pFields(routedRequest.ReceivedFromNeighborNullable);

                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending CFM to source peer, waiting for NPACK");
                    await routedRequest.ReceivedFromNeighborNullable.SendUdpRequestAsync_Retransmit_WaitForNPACK("cvm 1234589", cfmUdpData, cfm.ReqP2pSeq16, cfm.GetSignedFieldsForNeighborHMAC);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received NPACK to CFM");

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
                    _ = ReceiveShortSingleMessageAsync(session, req);
                }
                else
                    session.Dispose(); // todo implement other things
            }
            catch (Exception exc)
            {
                logger.WriteToLog_mediumPain($"could not accept INVITE request: {exc}");
            }
            finally
            {
                _pendingInviteRequests.Remove(req.RequesterRegistrationId);
            }
        }
                     

        async Task ReceiveShortSingleMessageAsync(InviteSession session, InviteRequestPacket req)
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
            _drpPeerApp.OnReceivedShortSingleMessage(receivedMessage, req);
        }
    }
}
