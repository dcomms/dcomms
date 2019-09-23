using Dcomms.Cryptography;
using Dcomms.DMP.Packets;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
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
                       
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
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
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            writeSignedFields(w);
            return GetPingPongHMAC(ms.ToArray());
        }
        #endregion

        byte[] MessageHMACkey;
        #region DirectChannelSharedDhSecrets  A+E
        byte[] DirectChannelSharedDhSecretA;
        byte[] LocalDirectChannelEcdhePrivateKeyA, LocalDirectChannelEcdhePublicKeyA;

        byte[] DirectChannelSharedDhSecretE;
        byte[] LocalDirectChannelEcdhePrivateKeyE, LocalDirectChannelEcdhePublicKeyE;

        bool _derivedDirectChannelSharedDhSecretsAE;
        void DeriveDirectChannelSharedDhSecretsAE(byte[] remoteDirectChannelEcdhePublicKeyA, byte[] remoteDirectChannelEcdhePublicKeyE)
        {
            if (!_derivedDirectChannelSharedDhSecretsAE)
            {
                _derivedDirectChannelSharedDhSecretsAE = true;

                _localDrpPeer.Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(remoteDirectChannelEcdhePublicKeyA);
                _localDrpPeer.Engine.RecentUniquePublicEcdhKeys.AssertIsUnique(remoteDirectChannelEcdhePublicKeyE);

                DirectChannelSharedDhSecretA = _localDrpPeer.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalDirectChannelEcdhePrivateKeyA, remoteDirectChannelEcdhePublicKeyA);
                DirectChannelSharedDhSecretE = _localDrpPeer.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalDirectChannelEcdhePrivateKeyE, remoteDirectChannelEcdhePublicKeyE);

                if (SharedPingPongHmacKey == null) throw new InvalidOperationException();
                PacketProcedures.CreateBinaryWriter(out var msA, out var wA);
                wA.Write(SharedPingPongHmacKey);
                wA.Write(DirectChannelSharedDhSecretA);
                MessageHMACkey = _localDrpPeer.CryptoLibrary.GetHashSHA256(msA.ToArray());
              // not safe to write it to log    _localDrpPeer.Engine.WriteToLog_dc_detail($"derived keys: A={MiscProcedures.ByteArrayToString(DirectChannelSharedDhSecretA)}, E={MiscProcedures.ByteArrayToString(DirectChannelSharedDhSecretE)}"); ;
            }
        }

        public HMAC GetMessageHMAC(byte[] data)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
            if (MessageHMACkey == null) throw new InvalidOperationException();
            var r = new HMAC
            {
                hmacSha256 = _localDrpPeer.CryptoLibrary.GetSha256HMAC(MessageHMACkey, data)
            };

            return r;
        }
        public HMAC GetMessageHMAC(Action<BinaryWriter> writeSignedFields)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
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
        public InviteSession(LocalDrpPeer localDrpPeer)
        {
            _localDrpPeer = localDrpPeer;
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalInviteAckEcdhePrivateKey, out LocalInviteAckEcdhePublicKey);
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalDirectChannelEcdhePrivateKeyA, out LocalDirectChannelEcdhePublicKeyA);
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalDirectChannelEcdhePrivateKeyE, out LocalDirectChannelEcdhePublicKeyE);

            DirectChannelToken32 localDirectChannelToken32 = null;
            for (int i = 0; i < 100; i++)
            {
                localDirectChannelToken32 = new DirectChannelToken32 { Token32 = (uint)localDrpPeer.Engine.InsecureRandom.Next() };
                var token16 = localDirectChannelToken32.Token16;
                if (localDrpPeer.Engine.InviteSessionsByToken16[token16] == null)
                {
                    localDrpPeer.Engine.InviteSessionsByToken16[token16] = this;
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

               
        #region ping pong packets
        internal void OnReceivedDmpPing(IPEndPoint remoteEndpoint, byte[] udpData) // engine thread
        {
            if (!remoteEndpoint.Equals(RemoteSessionDescription.DirectChannelEndPoint))
                throw new PossibleMitmException();

            var ping = DmpPingPacket.DecodeAndVerify(udpData, this);

            var pong = new DmpPongPacket
            {
                DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                PingRequestId32 = ping.PingRequestId32,                
            };
            if (ping.PublicEcdheKeyA != null)
            {
                pong.PublicEcdheKeyA = new EcdhPublicKey { Ecdh25519PublicKey = this.LocalDirectChannelEcdhePublicKeyA };
                pong.PublicEcdheKeyE = new EcdhPublicKey { Ecdh25519PublicKey = this.LocalDirectChannelEcdhePublicKeyE };
                this.DeriveDirectChannelSharedDhSecretsAE(ping.PublicEcdheKeyA.Ecdh25519PublicKey, ping.PublicEcdheKeyE.Ecdh25519PublicKey);
            }
            pong.PingPongHMAC = GetPingPongHMAC(pong.GetSignedFieldsForPingPongHMAC);

            var pongUdpData = pong.Encode();
            _localDrpPeer.Engine.SendPacket(pongUdpData, remoteEndpoint);
        }


        public DmpPingPacket CreatePing(bool sendPublicEcdhKeysAE)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
            var r = new DmpPingPacket
            {
                DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                PingRequestId32 = (uint)_insecureRandom.Next(),
            };
            if (sendPublicEcdhKeysAE)
            {
                r.PublicEcdheKeyA = new EcdhPublicKey { Ecdh25519PublicKey = this.LocalDirectChannelEcdhePublicKeyA };
                r.PublicEcdheKeyE = new EcdhPublicKey { Ecdh25519PublicKey = this.LocalDirectChannelEcdhePublicKeyE };
            }
            r.PingPongHMAC = GetPingPongHMAC(r.GetSignedFieldsForPingPongHMAC);
            return r;
        }

        #endregion

        internal async Task SetupAEkeysAsync()
        {
            var ping = CreatePing(true);
            var pingData = ping.Encode();

            var pongUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(pingData, RemoteSessionDescription.DirectChannelEndPoint,
                DmpPongPacket.GetScanner(LocalDirectChannelToken32, ping.PingRequestId32, this)); // scanner also verifies HMAC
            var pong = DmpPongPacket.Decode(pongUdpData);

            this.DeriveDirectChannelSharedDhSecretsAE(pong.PublicEcdheKeyA.Ecdh25519PublicKey, pong.PublicEcdheKeyE.Ecdh25519PublicKey);                      
        }


        internal async Task SendShortSingleMessageAsync(string messageText, UserCertificate senderUserCertificateWithPrivateKeys)
        {
            var messageSession = new MessageSession();
            var messageStart = new MessageStartPacket()
            {
                MessageId32 = (uint)_insecureRandom.Next(),
                DirectChannelToken32 = RemoteSessionDescription.DirectChannelToken32,
                MessageTimestamp64 = _localDrpPeer.Engine.Timestamp64,                
            };
            messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecretE);
            messageStart.EncryptedMessageData = messageSession.EncryptShortSingleMessage(_localDrpPeer.CryptoLibrary, messageText);

            // sign with HMAC
            messageStart.MessageHMAC = GetMessageHMAC(w => messageStart.GetSignedFieldsForMessageHMAC(w, true));

            // send msgstart, wait for msgack
            var messageStartUdpData = messageStart.Encode();
            var messageAckUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(messageStartUdpData, RemoteSessionDescription.DirectChannelEndPoint,
                MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.encryptionDecryptionCompleted) // scanner also verifies HMAC
                );
            var messageAck = MessageAckPacket.Decode(messageAckUdpData);

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
            await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(messagePartUdpData,
                RemoteSessionDescription.DirectChannelEndPoint,
                MessageAckPacket.GetScanner(messageStart.MessageId32, this, MessageSessionStatusCode.finalSignatureVerified) // scanner also verifies HMAC
                );
        }

        internal async Task<string> ReceiveShortSingleMessageAsync(UserCertificate senderUserCertificate)
        {
            var messageSession = new MessageSession();

            // wait for msgstart
            var messageStartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, RemoteSessionDescription.DirectChannelEndPoint,
                MessageStartPacket.GetScanner(LocalDirectChannelToken32, this) // scanner verifies MessageHMAC
                );
            var messageStart = MessageStartPacket.Decode(messageStartUdpData);
            
            messageSession.DeriveKeys(_localDrpPeer.CryptoLibrary, SharedPingPongHmacKey, messageStart, DirectChannelSharedDhSecretE);

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
            _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messageStart.DecodedUdpData, messageAckUdpData, RemoteSessionDescription.DirectChannelEndPoint);

            // wait for msgpart with status=encryptionDecryptionCompleted
            var messagePartUdpData = await _localDrpPeer.Engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, 
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
                throw new BadSignatureException();

            // send msgack status=finalSignatureVerified
            messageSession.Status = MessageSessionStatusCode.finalSignatureVerified;
            var messageAck_finalSignatureVerified = new MessageAckPacket
            {
                MessageId32 = messageStart.MessageId32,
                ReceiverStatus = messageSession.Status,
            };
            messageAck_finalSignatureVerified.MessageHMAC = GetMessageHMAC(messageAck_finalSignatureVerified.GetSignedFieldsForMessageHMAC);
            var messageAck_finalSignatureVerified_UdpData = messageAck_finalSignatureVerified.Encode();
            _localDrpPeer.Engine.RespondToRequestAndRetransmissions(messagePart.DecodedUdpData, messageAck_finalSignatureVerified_UdpData, RemoteSessionDescription.DirectChannelEndPoint);
                       
            return receivedMessage;
        }
    }
}
