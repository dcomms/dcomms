using Dcomms.Cryptography;
using Dcomms.DMP.Packets;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DMP
{
    public class InviteSession: IDisposable
    {
        public InviteSessionDescription LocalSessionDescription { get; set; }
        public InviteSessionDescription RemoteSessionDescription { get; set; }

        #region SharedPingPongHmacKey
        byte[] SharedPingPongHmacKey;
        internal void DeriveSharedPingPongHmacKey(InviteRequestPacket req, InviteAck1Packet ack1, InviteAck2Packet ack2, InviteConfirmationPacket cfm)
        {
            if (SharedInviteAckDhSecret == null) throw new NotImplementedException();
                       
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            req.GetSharedSignedFields(w);
            ack1.GetSharedSignedFields(w, true);
            ack2.GetSharedSignedFields(w);
            cfm.GetSharedSignedFields(w);
            w.Write(SharedInviteAckDhSecret);

            SharedPingPongHmacKey = _localDrpPeer.CryptoLibrary.GetHashSHA256(ms.ToArray());
        }

        public HMAC GetPingPongHMAC(byte[] data)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
            if (SharedPingPongHmacKey == null) throw new InvalidOperationException();
            var r = new HMAC
            {
                hmacSha256 = _localDrpPeer.CryptoLibrary.GetSha256HMAC(SharedPingPongHmacKey, data)
            };

            return r;
        }
        public HMAC GetPingPongHMAC(Action<BinaryWriter> writeSignedFields)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            writeSignedFields(w);
            return GetPingPongHMAC(ms.ToArray());
        }
        #endregion

        byte[] MessageHMACkey;
        #region DirectChannelSharedDhSecrets  A+E
        byte[] DirectChannelSharedDhSecret;
        byte[] LocalDirectChannelEcdhePrivateKey, LocalDirectChannelEcdhePublicKey;

        //byte[] DirectChannelSharedDhSecretE;
        //byte[] LocalDirectChannelEcdhePrivateKeyE, LocalDirectChannelEcdhePublicKeyE;

        bool _derivedDirectChannelSharedDhSecretsAE;
        public bool DerivedDirectChannelSharedDhSecretsAE => _derivedDirectChannelSharedDhSecretsAE;
        void DeriveDirectChannelSharedDhSecret(byte[] remoteDirectChannelEcdhePublicKey)
        {
            if (!_derivedDirectChannelSharedDhSecretsAE)
            {
                _derivedDirectChannelSharedDhSecretsAE = true;

                _localDrpPeer.Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(remoteDirectChannelEcdhePublicKey, $"remoteDirectChannelEcdhePublicKey {RemoteSessionDescription}");
                DirectChannelSharedDhSecret = _localDrpPeer.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalDirectChannelEcdhePrivateKey, remoteDirectChannelEcdhePublicKey);
                
                if (SharedPingPongHmacKey == null) throw new InvalidOperationException();
                BinaryProcedures.CreateBinaryWriter(out var msA, out var wA);
                wA.Write(SharedPingPongHmacKey);
                wA.Write(DirectChannelSharedDhSecret);
                MessageHMACkey = _localDrpPeer.CryptoLibrary.GetHashSHA256(msA.ToArray());
              // not safe to write it to log    _localDrpPeer.Engine.WriteToLog_dc_detail($"derived keys: A={MiscProcedures.ByteArrayToString(DirectChannelSharedDhSecretA)}, E={MiscProcedures.ByteArrayToString(DirectChannelSharedDhSecretE)}"); ;
            }
        }

        public HMAC GetMessageHMAC(byte[] data)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
            if (DerivedDirectChannelSharedDhSecretsAE == false) throw new InvalidOperationException("DerivedDirectChannelSharedDhSecretsAE=false");
            if (MessageHMACkey == null) throw new InvalidOperationException($"MessageHMACkey = null, DerivedDirectChannelSharedDhSecretsAE={DerivedDirectChannelSharedDhSecretsAE}");
            var r = new HMAC
            {
                hmacSha256 = _localDrpPeer.CryptoLibrary.GetSha256HMAC(MessageHMACkey, data)
            };

            return r;
        }
        public HMAC GetMessageHMAC(Action<BinaryWriter> writeSignedFields)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            writeSignedFields(w);
            return GetMessageHMAC(ms.ToArray());
        }
        #endregion

        #region SharedInviteAckDhSecret
        readonly byte[] LocalInviteAckEcdhePrivateKey;
        readonly public byte[] LocalInviteAckEcdhePublicKey;
        internal byte[] SharedInviteAckDhSecret;

        internal void DeriveSharedInviteAckDhSecret(ICryptoLibrary cryptoLibrary, byte[] remotePublicEcdheKey)
        {
            SharedInviteAckDhSecret = cryptoLibrary.DeriveEcdh25519SharedSecret(LocalInviteAckEcdhePrivateKey, remotePublicEcdheKey);
        }
        #endregion

        readonly Random _insecureRandom = new Random();
        readonly LocalDrpPeer _localDrpPeer;
        internal readonly DirectChannelToken32 LocalDirectChannelToken32;
        bool _disposed;
        public Logger Logger;
        public InviteSession(LocalDrpPeer localDrpPeer)
        {
            _localDrpPeer = localDrpPeer;
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalInviteAckEcdhePrivateKey, out LocalInviteAckEcdhePublicKey);
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalDirectChannelEcdhePrivateKey, out LocalDirectChannelEcdhePublicKey);

            DirectChannelToken32 localDirectChannelToken32 = null;
            for (int i = 0; i < 100; i++)
            {
                localDirectChannelToken32 = new DirectChannelToken32 { Token32 = (uint)localDrpPeer.Engine.InsecureRandom.Next() };
                var token16 = localDirectChannelToken32.Token16;
                if (localDrpPeer.Engine.InviteSessionsByToken16[token16] == null)
                {
                    localDrpPeer.Engine.InviteSessionsByToken16[token16] = this;
                    break;
                }
            }
            if (localDirectChannelToken32 == null) throw new InsufficientResourcesException();
            LocalDirectChannelToken32 = localDirectChannelToken32;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _localDrpPeer.Engine.InviteSessionsByToken16[LocalDirectChannelToken32.Token16] = null;
        }
        public override string ToString() => $"inv{LocalDirectChannelToken32}";

        #region vision

        internal void WriteToLog_detail(string message)
        {
            var config = _localDrpPeer.Engine.Configuration;
            if (config.VisionChannel?.GetAttentionTo(config.VisionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_inv) <= AttentionLevel.detail)
                config.VisionChannel?.Emit(config.VisionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_inv, AttentionLevel.detail, $"[{this}] {message}");
        }
        #endregion


        #region ping pong packets
        internal void OnReceivedDmpPing(IPEndPoint remoteEndpoint, byte[] udpData) // engine thread
        {
            WriteToLog_detail($">> OnReceivedDmpPing(remoteEndpoint={remoteEndpoint})");
            
            if (!remoteEndpoint.Address.Equals(RemoteSessionDescription.DirectChannelEndPoint.Address))
                throw new PossibleAttackException($"receibed DMP PING from bad IP address {remoteEndpoint.Address}, expected from {RemoteSessionDescription.DirectChannelEndPoint.Address}");
            
            if (SharedPingPongHmacKey == null)
            {
                WriteToLog_detail($"ignoring received DMP PING: SharedPingPongHmacKey is not initialized yet");
                return;
            }

            var ping = DmpPingPacket.DecodeAndVerify(udpData, this);


            if (this.RemoteSessionDescription.DirectChannelEndPoint.Port != remoteEndpoint.Port)
            {
                WriteToLog_detail($"updating remote peer DirectChannel port from {this.RemoteSessionDescription.DirectChannelEndPoint} to {remoteEndpoint} (when remote peer opens another port in NAT)");
                this.RemoteSessionDescription.DirectChannelEndPoint = remoteEndpoint;
                if (this.InitialPendingPingRequest != null)
                    this.InitialPendingPingRequest.ResponderEndpoint = remoteEndpoint;
            }


            var pong = new DmpPongPacket
            {
                DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                PingRequestId32 = ping.PingRequestId32,                
            };
            if (ping.PublicEcdheKey != null)
            {
                pong.PublicEcdheKey = new EcdhPublicKey { Ecdh25519PublicKey = this.LocalDirectChannelEcdhePublicKey };
                this.DeriveDirectChannelSharedDhSecret(ping.PublicEcdheKey.Ecdh25519PublicKey);
            }
            pong.PingPongHMAC = GetPingPongHMAC(pong.GetSignedFieldsForPingPongHMAC);

            var pongUdpData = pong.Encode();
            _localDrpPeer.Engine.SendPacket(pongUdpData, remoteEndpoint);
        }


        public DmpPingPacket CreatePing(bool sendPublicEcdhKey)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
            var r = new DmpPingPacket
            {
                DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                PingRequestId32 = (uint)_insecureRandom.Next(),
            };
            if (sendPublicEcdhKey)
            {
                r.PublicEcdheKey = new EcdhPublicKey { Ecdh25519PublicKey = this.LocalDirectChannelEcdhePublicKey };
            }
            r.PingPongHMAC = GetPingPongHMAC(r.GetSignedFieldsForPingPongHMAC);
            return r;
        }

        #endregion

        PendingLowLevelUdpRequest InitialPendingPingRequest;
        internal async Task SetupAEkeysAsync()
        {
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail(">> InviteSession.SetupAEkeysAsync()");
            var ping = CreatePing(true);
            var pingData = ping.Encode();


            var timeoutS = _localDrpPeer.Engine.Configuration.UdpLowLevelRequests_ExpirationTimeoutS;
            InitialPendingPingRequest = new PendingLowLevelUdpRequest("dmp pong 3186", RemoteSessionDescription.DirectChannelEndPoint,
                         DmpPongPacket.GetScanner(LocalDirectChannelToken32, ping.PingRequestId32, this), // scanner also verifies HMAC 
                         _localDrpPeer.Engine.DateTimeNowUtc, timeoutS,
                         pingData,
                         _localDrpPeer.Engine.Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS, _localDrpPeer.Engine.Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                     );
            var pongUdpData = await _localDrpPeer.Engine.SendUdpRequestAsync_Retransmit(InitialPendingPingRequest);
            if (pongUdpData == null)
            {
                string desc = $"no response to DC PING from {RemoteSessionDescription.DirectChannelEndPoint}  - timeout expired ({timeoutS}s)";
                throw new DrpTimeoutException(desc);
            }        

            var pong = DmpPongPacket.Decode(pongUdpData);

            this.DeriveDirectChannelSharedDhSecret(pong.PublicEcdheKey.Ecdh25519PublicKey);
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("<< InviteSession.SetupAEkeysAsync()");
        }

        #region ShortSingleMessage
        internal async Task SendShortSingleMessageAsync(string messageText, UserCertificate senderUserCertificateWithPrivateKeys)
        {
            if (!DerivedDirectChannelSharedDhSecretsAE) throw new InvalidOperationException("DerivedDirectChannelSharedDhSecretsAE = false");
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail(">> InviteSession.SendShortSingleMessageAsync()");
            
            var messageSession = new MessageSession();
            var messageStart = new MessageStartPacket()
            {
                MessageId32 = (uint)_insecureRandom.Next(),
                DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                MessageTimestamp64 = _localDrpPeer.Engine.Timestamp64,                
            };
            messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecret);
            messageStart.EncryptedMessageData = messageSession.EncryptShortSingleMessage(_localDrpPeer.CryptoLibrary, messageText);

            // sign with HMAC
            messageStart.MessageHMAC = GetMessageHMAC(w => messageStart.GetSignedFieldsForMessageHMAC(w, true));

            // send msgstart, wait for msgack
            var messageStartUdpData = messageStart.Encode();
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGSTART and waiting for MSGACK");
            var messageAckUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgstart 4582", "remote user",
                messageStartUdpData, RemoteSessionDescription.DirectChannelEndPoint,
                MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted) // scanner also verifies HMAC
                );
            var messageAck = MessageAckPacket.Decode(messageAckUdpData);
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("decoded MSGACK");

            // send msgpart with status=encryptionDecryptionCompleted and signature
            if (messageSession.Status != MessageSessionStatusCode.encryptionDecryptionCompleted) throw new InvalidOperationException();
            var messagePart = new MessagePartPacket
            {
                MessageId32 = messageStart.MessageId32,
                SenderStatus = messageSession.Status,
                SenderSignature = UserCertificateSignature.Sign(_localDrpPeer.CryptoLibrary, w =>
                {
                    w.Write(MessageHMACkey);
                    w.Write(messageSession.AesKey);
                    w.Write(messageStart.EncryptedMessageData);
                    w.Write(messageAck.ReceiverFinalNonce);
                }, senderUserCertificateWithPrivateKeys)
            };
            messagePart.MessageHMAC = GetMessageHMAC(messagePart.GetSignedFieldsForMessageHMAC);            
            var messagePartUdpData = messagePart.Encode();

            // wait for msgack status=finalSignatureVerified
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGPART with message data, waiting for MSGACK");
            await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgpart 3681", "remote user", messagePartUdpData,
                RemoteSessionDescription.DirectChannelEndPoint,
                MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.finalSignatureVerified) // scanner also verifies HMAC
                );
        }
        internal async Task<string> ReceiveShortSingleMessageAsync(UserCertificate senderUserCertificate)
        {
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail(">>InviteSession.ReceiveShortSingleMessageAsync()");
            if (!DerivedDirectChannelSharedDhSecretsAE) throw new InvalidOperationException("DerivedDirectChannelSharedDhSecretsAE = false");
            
            var messageSession = new MessageSession();

            // wait for msgstart
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("waiting for MSGSTART");
            var messageStartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgstart 480", "remote user", null, RemoteSessionDescription.DirectChannelEndPoint,
                MessageStartPacket.GetScanner(LocalDirectChannelToken32, this) // scanner verifies MessageHMAC
                );
            var messageStart = MessageStartPacket.Decode(messageStartUdpData);
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("decoded MSGSTART");

            messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecret);

            // decrypt
            var receivedMessage = messageSession.DecryptShortSingleMessage(_localDrpPeer.CryptoLibrary, messageStart.EncryptedMessageData);
            
            // respond with msgack + ReceiverFinalNonce
            if (messageSession.Status != MessageSessionStatusCode.encryptionDecryptionCompleted) throw new InvalidOperationException();
            var messageAck = new MessageAckPacket
            {
                MessageId32 = messageStart.MessageId32,
                ReceiverStatus = messageSession.Status,
                ReceiverFinalNonce = _localDrpPeer.CryptoLibrary.GetRandomBytes(MessageAckPacket.ReceiverFinalNonceSize),
            };
            messageAck.MessageHMAC = GetMessageHMAC(messageAck.GetSignedFieldsForMessageHMAC);
            var messageAckUdpData = messageAck.Encode();
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("responding with MSGACK to MSGSTART");
            _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messageStart.DecodedUdpData, messageAckUdpData, RemoteSessionDescription.DirectChannelEndPoint);

            // wait for msgpart with status=encryptionDecryptionCompleted
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("waiting for with MSGPART");
            var messagePartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgpart 42486", "remote user", null, 
                RemoteSessionDescription.DirectChannelEndPoint,
                MessagePartPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted)
                // scanner verifies MessageHMAC
                );
            var messagePart = MessagePartPacket.Decode(messagePartUdpData);

            // verify signature of sender user
            if (!messagePart.SenderSignature.Verify(_localDrpPeer.CryptoLibrary, w =>
            {
                w.Write(MessageHMACkey);
                w.Write(messageSession.AesKey);
                w.Write(messageStart.EncryptedMessageData);
                w.Write(messageAck.ReceiverFinalNonce);
            }, senderUserCertificate))
                throw new BadSignatureException("bad MSGPART sender signature 3408");
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("verified sender signature");

            // send msgack status=finalSignatureVerified
            messageSession.Status = MessageSessionStatusCode.finalSignatureVerified;
            var messageAck_finalSignatureVerified = new MessageAckPacket
            {
                MessageId32 = messageStart.MessageId32,
                ReceiverStatus = messageSession.Status,
            };
            messageAck_finalSignatureVerified.MessageHMAC = GetMessageHMAC(messageAck_finalSignatureVerified.GetSignedFieldsForMessageHMAC);
            var messageAck_finalSignatureVerified_UdpData = messageAck_finalSignatureVerified.Encode();
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGACK response to MSGPART");
            _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messagePart.DecodedUdpData, messageAck_finalSignatureVerified_UdpData, RemoteSessionDescription.DirectChannelEndPoint);
                       
            return receivedMessage;
        }
        #endregion


        /// <returns>remote user ID, reg IDs</returns>
        internal async Task<(UserId, RegistrationId[])> ExchangeContactInvitationsAsync_AtInviteRequester(UserCertificate localUserCertificateWithPrivateKeys, UserId localUserId, RegistrationId[] localRegistrationIds, UserCertificate remoteUserCertificate)
        {
            if (!DerivedDirectChannelSharedDhSecretsAE) throw new InvalidOperationException("DerivedDirectChannelSharedDhSecretsAE = false");
            if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail(">> InviteSession.ExchangeContactInvitationsAsync_AtInviteRequester()");

            { // send MSG with contact invitation
                var messageSession = new MessageSession();
                var messageStart = new MessageStartPacket()
                {
                    MessageId32 = (uint)_insecureRandom.Next(),
                    DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                    MessageTimestamp64 = _localDrpPeer.Engine.Timestamp64,
                };
                messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecret);
                messageStart.EncryptedMessageData = messageSession.EncryptContactInvitation(_localDrpPeer.CryptoLibrary, localUserId, localRegistrationIds);

                // sign with HMAC
                messageStart.MessageHMAC = GetMessageHMAC(w => messageStart.GetSignedFieldsForMessageHMAC(w, true));

                // send msgstart, wait for msgack
                var messageStartUdpData = messageStart.Encode();
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGSTART and waiting for MSGACK");
                var messageAckUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgstart 2345", "remote user",
                    messageStartUdpData, RemoteSessionDescription.DirectChannelEndPoint,
                    MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted) // scanner also verifies HMAC
                    );
                var messageAck = MessageAckPacket.Decode(messageAckUdpData);
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("decoded MSGACK");

                // send msgpart with status=encryptionDecryptionCompleted and signature
                if (messageSession.Status != MessageSessionStatusCode.encryptionDecryptionCompleted) throw new InvalidOperationException();
                var messagePart = new MessagePartPacket
                {
                    MessageId32 = messageStart.MessageId32,
                    SenderStatus = messageSession.Status,
                    SenderSignature = UserCertificateSignature.Sign(_localDrpPeer.CryptoLibrary, w =>
                    {
                        w.Write(MessageHMACkey);
                        w.Write(messageSession.AesKey);
                        w.Write(messageStart.EncryptedMessageData);
                        w.Write(messageAck.ReceiverFinalNonce);
                    }, localUserCertificateWithPrivateKeys)
                };
                messagePart.MessageHMAC = GetMessageHMAC(messagePart.GetSignedFieldsForMessageHMAC);
                var messagePartUdpData = messagePart.Encode();

                // wait for msgack status=finalSignatureVerified
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGPART with message data, waiting for MSGACK");
                await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgpart 8178", "remote user", messagePartUdpData,
                    RemoteSessionDescription.DirectChannelEndPoint,
                    MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.finalSignatureVerified) // scanner also verifies HMAC
                    );
            }

          
            { // receive MSG with contact invitation
                var messageSession = new MessageSession();

                // wait for msgstart
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("waiting for MSGSTART");
                var messageStartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgstart 5678", "remote user", null, RemoteSessionDescription.DirectChannelEndPoint,
                    MessageStartPacket.GetScanner(LocalDirectChannelToken32, this) // scanner verifies MessageHMAC
                    );
                var messageStart = MessageStartPacket.Decode(messageStartUdpData);
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("decoded MSGSTART");

                messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecret);

                // decrypt
                var remoteInvitation = messageSession.DecryptContactInvitation(_localDrpPeer.CryptoLibrary, messageStart.EncryptedMessageData);

                // respond with msgack + ReceiverFinalNonce
                if (messageSession.Status != MessageSessionStatusCode.encryptionDecryptionCompleted) throw new InvalidOperationException();
                var messageAck = new MessageAckPacket
                {
                    MessageId32 = messageStart.MessageId32,
                    ReceiverStatus = messageSession.Status,
                    ReceiverFinalNonce = _localDrpPeer.CryptoLibrary.GetRandomBytes(MessageAckPacket.ReceiverFinalNonceSize),
                };
                messageAck.MessageHMAC = GetMessageHMAC(messageAck.GetSignedFieldsForMessageHMAC);
                var messageAckUdpData = messageAck.Encode();
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("responding with MSGACK to MSGSTART");
                _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messageStart.DecodedUdpData, messageAckUdpData, RemoteSessionDescription.DirectChannelEndPoint);

                // wait for msgpart with status=encryptionDecryptionCompleted
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("waiting for with MSGPART");
                var messagePartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgpart 7807", "remote user", null,
                    RemoteSessionDescription.DirectChannelEndPoint,
                    MessagePartPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted)
                    // scanner verifies MessageHMAC
                    );
                var messagePart = MessagePartPacket.Decode(messagePartUdpData);

                // verify signature of sender user
                if (!messagePart.SenderSignature.Verify(_localDrpPeer.CryptoLibrary, w =>
                {
                    w.Write(MessageHMACkey);
                    w.Write(messageSession.AesKey);
                    w.Write(messageStart.EncryptedMessageData);
                    w.Write(messageAck.ReceiverFinalNonce);
                }, remoteUserCertificate))
                    throw new BadSignatureException("bad MSGPART sender signature 5497");
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("verified sender signature");

                // send msgack status=finalSignatureVerified
                messageSession.Status = MessageSessionStatusCode.finalSignatureVerified;
                var messageAck_finalSignatureVerified = new MessageAckPacket
                {
                    MessageId32 = messageStart.MessageId32,
                    ReceiverStatus = messageSession.Status,
                };
                messageAck_finalSignatureVerified.MessageHMAC = GetMessageHMAC(messageAck_finalSignatureVerified.GetSignedFieldsForMessageHMAC);
                var messageAck_finalSignatureVerified_UdpData = messageAck_finalSignatureVerified.Encode();
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGACK response to MSGPART");
                _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messagePart.DecodedUdpData, messageAck_finalSignatureVerified_UdpData, RemoteSessionDescription.DirectChannelEndPoint);

                return remoteInvitation;
            }
        }
        


        /// <returns>remote user ID, reg IDs</returns>
        internal async Task<(UserId, RegistrationId[])> ExchangeContactInvitationsAsync_AtInviteResponder(UserCertificate localUserCertificateWithPrivateKeys, UserId localUserId, RegistrationId[] localRegistrationIds, UserCertificate remoteUserCertificate)
        {
            (UserId, RegistrationId[]) remoteInvitation;
            { // receive MSG with contact invitation
                var messageSession = new MessageSession();

                // wait for msgstart
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("waiting for MSGSTART");
                var messageStartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgstart 981", "remote user", null, RemoteSessionDescription.DirectChannelEndPoint,
                    MessageStartPacket.GetScanner(LocalDirectChannelToken32, this) // scanner verifies MessageHMAC
                    );
                var messageStart = MessageStartPacket.Decode(messageStartUdpData);
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("decoded MSGSTART");

                messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecret);

                // decrypt
                remoteInvitation = messageSession.DecryptContactInvitation(_localDrpPeer.CryptoLibrary, messageStart.EncryptedMessageData);

                // respond with msgack + ReceiverFinalNonce
                if (messageSession.Status != MessageSessionStatusCode.encryptionDecryptionCompleted) throw new InvalidOperationException();
                var messageAck = new MessageAckPacket
                {
                    MessageId32 = messageStart.MessageId32,
                    ReceiverStatus = messageSession.Status,
                    ReceiverFinalNonce = _localDrpPeer.CryptoLibrary.GetRandomBytes(MessageAckPacket.ReceiverFinalNonceSize),
                };
                messageAck.MessageHMAC = GetMessageHMAC(messageAck.GetSignedFieldsForMessageHMAC);
                var messageAckUdpData = messageAck.Encode();
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("responding with MSGACK to MSGSTART");
                _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messageStart.DecodedUdpData, messageAckUdpData, RemoteSessionDescription.DirectChannelEndPoint);

                // wait for msgpart with status=encryptionDecryptionCompleted
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("waiting for with MSGPART");
                var messagePartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgpart 2687", "remote user", null,
                    RemoteSessionDescription.DirectChannelEndPoint,
                    MessagePartPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted)
                    // scanner verifies MessageHMAC
                    );
                var messagePart = MessagePartPacket.Decode(messagePartUdpData);

                // verify signature of sender user
                if (!messagePart.SenderSignature.Verify(_localDrpPeer.CryptoLibrary, w =>
                {
                    w.Write(MessageHMACkey);
                    w.Write(messageSession.AesKey);
                    w.Write(messageStart.EncryptedMessageData);
                    w.Write(messageAck.ReceiverFinalNonce);
                }, remoteUserCertificate))
                    throw new BadSignatureException("bad MSGPART sender signature 98412");
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("verified sender signature");

                // send msgack status=finalSignatureVerified
                messageSession.Status = MessageSessionStatusCode.finalSignatureVerified;
                var messageAck_finalSignatureVerified = new MessageAckPacket
                {
                    MessageId32 = messageStart.MessageId32,
                    ReceiverStatus = messageSession.Status,
                };
                messageAck_finalSignatureVerified.MessageHMAC = GetMessageHMAC(messageAck_finalSignatureVerified.GetSignedFieldsForMessageHMAC);
                var messageAck_finalSignatureVerified_UdpData = messageAck_finalSignatureVerified.Encode();
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGACK response to MSGPART");
                _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messagePart.DecodedUdpData, messageAck_finalSignatureVerified_UdpData, RemoteSessionDescription.DirectChannelEndPoint);

            }


            { // send MSG with contact invitation
                var messageSession = new MessageSession();
                var messageStart = new MessageStartPacket()
                {
                    MessageId32 = (uint)_insecureRandom.Next(),
                    DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                    MessageTimestamp64 = _localDrpPeer.Engine.Timestamp64,
                };
                messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecret);
                messageStart.EncryptedMessageData = messageSession.EncryptContactInvitation(_localDrpPeer.CryptoLibrary, localUserId, localRegistrationIds);

                // sign with HMAC
                messageStart.MessageHMAC = GetMessageHMAC(w => messageStart.GetSignedFieldsForMessageHMAC(w, true));

                // send msgstart, wait for msgack
                var messageStartUdpData = messageStart.Encode();
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGSTART and waiting for MSGACK");
                var messageAckUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgstart 974", "remote user",
                    messageStartUdpData, RemoteSessionDescription.DirectChannelEndPoint,
                    MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted) // scanner also verifies HMAC
                    );
                var messageAck = MessageAckPacket.Decode(messageAckUdpData);
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("decoded MSGACK");

                // send msgpart with status=encryptionDecryptionCompleted and signature
                if (messageSession.Status != MessageSessionStatusCode.encryptionDecryptionCompleted) throw new InvalidOperationException();
                var messagePart = new MessagePartPacket
                {
                    MessageId32 = messageStart.MessageId32,
                    SenderStatus = messageSession.Status,
                    SenderSignature = UserCertificateSignature.Sign(_localDrpPeer.CryptoLibrary, w =>
                    {
                        w.Write(MessageHMACkey);
                        w.Write(messageSession.AesKey);
                        w.Write(messageStart.EncryptedMessageData);
                        w.Write(messageAck.ReceiverFinalNonce);
                    }, localUserCertificateWithPrivateKeys)
                };
                messagePart.MessageHMAC = GetMessageHMAC(messagePart.GetSignedFieldsForMessageHMAC);
                var messagePartUdpData = messagePart.Encode();

                // wait for msgack status=finalSignatureVerified
                if (Logger.WriteToLog_detail_enabled) Logger.WriteToLog_detail("sending MSGPART with message data, waiting for MSGACK");
                await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse("msgpart 34563", "remote user", messagePartUdpData,
                    RemoteSessionDescription.DirectChannelEndPoint,
                    MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.finalSignatureVerified) // scanner also verifies HMAC
                    );
            }




            return remoteInvitation;
        }
    }
}
