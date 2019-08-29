using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    public enum ConnectedDrpPeerInitiatedBy
    {
        localPeer, // local peer connected to remote peer via REGISTER procedure
        remotePeer // remote peer connected to local peer via REGISTER procedure
    }
    /// <summary>
    /// connected or connecting peer, connection to neighbor peer
    /// 
    /// contains:
    /// parameters to transmit DRP pings and proxied packets between registered neighbors:
    /// from local peer to remote peer (txParamaters)
    /// from remote peer to local peer (rxParamaters)
    /// is negotiated via REGISTER channel
    /// all fields are encrypted when transmitted over REGISTER channel, using single-block AES and shared ECDH key
    /// </summary>
    public class ConnectionToNeighbor: IDisposable
    {
        internal byte[] SharedAuthKeyForHMAC; //  this key is shared secret, known only at requester (A) and neighbor (N), it is used for HMAC
        RegisterSynPacket _syn;
        RegisterSynAckPacket _synAck;
        RegisterAckPacket _ack;

        #region tx parameters (parameters to transmit direct (p2p) packets from local peer to neighbor)
        public P2pConnectionToken32 RemotePeerToken32;
        public IPEndPoint RemoteEndpoint; // IP address + UDP port // where to send packets
        internal byte[] SharedDhSecret;

        /// <summary>
        /// initializes parameters to transmit direct (p2p) packets form requester A to neighbor N
        /// </summary>
        public void DecryptAtRegisterRequester(RegisterSynPacket localRegisterSyn, RegisterSynAckPacket remoteRegisterSynAck)
        {
            if ((remoteRegisterSynAck.Flags & RegisterSynAckPacket.Flag_ipv6) != 0) throw new NotImplementedException();
            
            SharedDhSecret = _engine.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhe25519PrivateKey, remoteRegisterSynAck.ResponderEcdhePublicKey.ecdh25519PublicKey);

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                localRegisterSyn.GetCommonRequesterProxierResponderFields(writer, true);
                remoteRegisterSynAck.GetCommonRequesterProxierResponderFields(writer, false, false);
           
                var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();

                ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
                var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

                var toNeighborTxParametersDecrypted = new byte[remoteRegisterSynAck.ToResponderTxParametersEncrypted.Length];
                _engine.CryptoLibrary.ProcessSingleAesBlock(false, aesKey, iv, remoteRegisterSynAck.ToResponderTxParametersEncrypted, toNeighborTxParametersDecrypted);

                // parse toNeighborTxParametersDecrypted
                using (var reader = new BinaryReader(new MemoryStream(toNeighborTxParametersDecrypted)))
                {
                    RemoteEndpoint = PacketProcedures.DecodeIPEndPointIpv4(reader);
                    RemotePeerToken32 = P2pConnectionToken32.Decode(reader);
                    var magic16 = reader.ReadUInt16();
                    if (magic16 != Magic16_ipv4_responderToRequester) throw new BrokenCipherException();
                }
            
                _engine.WriteToLog_reg_requesterSide_detail($"decrypted remote endpoint={RemoteEndpoint}, remotePeerToken={RemotePeerToken32}");
            }
        }
        const ushort Magic16_ipv4_responderToRequester = 0x60C1; // is used to validate decrypted data
        
        /// <summary>
        /// when sending SYN-ACK
        /// </summary>
        public byte[] EncryptAtRegisterResponder(RegisterSynPacket remoteRegisterSyn, RegisterSynAckPacket localRegisterSynAck)
        {
            if (remoteRegisterSyn.AtoEP == false) throw new NotImplementedException();
            if (remoteRegisterSyn.EpEndpoint.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new NotImplementedException();

            SharedDhSecret = _engine.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhe25519PrivateKey, remoteRegisterSyn.RequesterEcdhePublicKey.ecdh25519PublicKey);

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                remoteRegisterSyn.GetCommonRequesterProxierResponderFields(writer, true);
                localRegisterSynAck.GetCommonRequesterProxierResponderFields(writer, false, false);

                var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();;
                ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);

                var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
                           
                // encode localRxParameters
                PacketProcedures.CreateBinaryWriter(out var msRxParameters, out var wRxParameters);
                PacketProcedures.EncodeIPEndPointIpv4(wRxParameters, remoteRegisterSyn.EpEndpoint); // 6
                LocalRxToken32.Encode(wRxParameters); // 4
                wRxParameters.Write(Magic16_ipv4_responderToRequester);    // 2
                wRxParameters.Write(_engine.CryptoLibrary.GetRandomBytes(4));      // 4

                var localRxParametersDecrypted = msRxParameters.ToArray(); // total 16 bytes
                var localRxParametersEncrypted = new byte[localRxParametersDecrypted.Length];
                _engine.CryptoLibrary.ProcessSingleAesBlock(true, aesKey, iv, localRxParametersDecrypted, localRxParametersEncrypted);

                return localRxParametersEncrypted;
            }

        }
               
        /// <summary>initializes parameters to transmit direct (p2p) packets form neighbor N to requester A</returns>
        public void DecryptAtRegisterResponder(RegisterSynPacket remoteRegisterSyn, RegisterSynAckPacket localRegisterSynAck, RegisterAckPacket remoteRegisterAck)
        {
            if ((remoteRegisterAck.Flags & RegisterSynAckPacket.Flag_ipv6) != 0) throw new NotImplementedException();
            
            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                remoteRegisterSyn.GetCommonRequesterProxierResponderFields(writer, true);
                localRegisterSynAck.GetCommonRequesterProxierResponderFields(writer, true, true);
                remoteRegisterAck.GetCommonRequesterProxierResponderFields(writer, false, false);
            
                var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();

                ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
                var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp


                var toRequesterTxParametersDecrypted = new byte[remoteRegisterAck.ToRequesterTxParametersEncrypted.Length];
                _engine.CryptoLibrary.ProcessSingleAesBlock(false, aesKey, iv, remoteRegisterAck.ToRequesterTxParametersEncrypted, toRequesterTxParametersDecrypted);

                // parse toRequesterTxParametersDecrypted
                using (var reader = new BinaryReader(new MemoryStream(toRequesterTxParametersDecrypted)))
                {
                    RemoteEndpoint = PacketProcedures.DecodeIPEndPointIpv4(reader);
                    RemotePeerToken32 = P2pConnectionToken32.Decode(reader);
                    var magic16 = reader.ReadUInt16();
                    if (magic16 != Magic16_ipv4_requesterToResponder) throw new BrokenCipherException();
                }

                InitializeNeighborTxRxStreams(remoteRegisterSyn, localRegisterSynAck, remoteRegisterAck);
            }
        }

        /// <summary>
        /// when sending ACK
        /// </summary>
        public byte[] EncryptAtRegisterRequester(RegisterSynPacket localRegisterSyn, RegisterSynAckPacket remoteRegisterSynAck, RegisterAckPacket localRegisterAckPacket)
        {
            if ((remoteRegisterSynAck.Flags & RegisterSynAckPacket.Flag_ipv6) != 0) throw new NotImplementedException();

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                localRegisterSyn.GetCommonRequesterProxierResponderFields(writer, true);
                remoteRegisterSynAck.GetCommonRequesterProxierResponderFields(writer, true, true);
                localRegisterAckPacket.GetCommonRequesterProxierResponderFields(writer, false, false);
           
                var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();

                ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
                var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

                // encode localRxParameters
                PacketProcedures.CreateBinaryWriter(out var msRxParameters, out var wRxParameters);
                PacketProcedures.EncodeIPEndPointIpv4(wRxParameters, LocalEndpoint); // 6
                LocalRxToken32.Encode(wRxParameters); // 4
                wRxParameters.Write(Magic16_ipv4_requesterToResponder);    // 2
                wRxParameters.Write(_engine.CryptoLibrary.GetRandomBytes(4));      // 4

                var localRxParametersDecrypted = msRxParameters.ToArray(); // total 16 bytes
                var localRxParametersEncrypted = new byte[localRxParametersDecrypted.Length];
                _engine.CryptoLibrary.ProcessSingleAesBlock(true, aesKey, iv, localRxParametersDecrypted, localRxParametersEncrypted);

                return localRxParametersEncrypted;
            }
        }
        const ushort Magic16_ipv4_requesterToResponder = 0xBFA4; // is used to validate decrypted data



        //   IAuthenticatedEncryptor Encryptor;
        //   IAuthenticatedDecryptor Decryptor;

        /// <summary>
        /// initializes SharedAuthKeyForHMAC
        /// </summary>
        public void InitializeNeighborTxRxStreams(RegisterSynPacket syn, RegisterSynAckPacket synAck, RegisterAckPacket ack)
        {
            if (_disposed) throw new ObjectDisposedException(_name);

            _syn = syn;
            _synAck = synAck;
            _ack = ack;

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                syn.GetCommonRequesterProxierResponderFields(writer, true);
                synAck.GetCommonRequesterProxierResponderFields(writer, true, true);
                ack.GetCommonRequesterProxierResponderFields(writer, false, true);
           
                //  var iv = cryptoLibrary.GetHashSHA256(ms.ToArray()); // todo use for encryption

                ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);

                SharedAuthKeyForHMAC = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

                //Encryptor = cryptoLibrary.CreateAesEncyptor(iv, aesKey);
                //Decryptor = cryptoLibrary.CreateAesDecyptor(iv, aesKey);
            }
        }
        public HMAC GetSharedHmac(byte[] data)
        {
            if (_disposed) throw new ObjectDisposedException(_name);
            if (SharedAuthKeyForHMAC == null) throw new InvalidOperationException();
            var r = new HMAC
            {
                hmacSha256 = _engine.CryptoLibrary.GetSha256HMAC(SharedAuthKeyForHMAC, data)
            };
            
          //  Engine.WriteToLog_ping_detail($"<< GetSharedHmac(input={MiscProcedures.ByteArrayToString(data)}, sha256={MiscProcedures.ByteArrayToString(_engine.CryptoLibrary.GetHashSHA256(data))}) returns {r}. SharedAuthKeyForHMAC={MiscProcedures.ByteArrayToString(SharedAuthKeyForHMAC)}");
            return r;
        }
        public HMAC GetSharedHmac(Action<BinaryWriter> data)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            data(w);
            return GetSharedHmac(ms.ToArray());
        }
        #endregion
               
        public ConnectedDrpPeerInitiatedBy InitiatedBy;
        RegistrationPublicKey _remotePeerPublicKey;
        public RegistrationPublicKey RemotePeerPublicKey
        {
            get => _remotePeerPublicKey;
            set
            {
                _remotePeerPublicKey = value;
                _name = $"connTo{RemotePeerPublicKey}";
            }
        }
        string _name = "ConnectionToNeighbor";
        public override string ToString() => _name;
        public readonly P2pConnectionToken32 LocalRxToken32; // is generated by local peer
        /// <summary>
        /// ip address and port of local peer, which _can_ be accessible by remote peers via internet
        /// </summary>
        public IPEndPoint LocalEndpoint;
        
      //  ConnectedDrpPeerRating Rating;
        readonly Random _rngForPingRequestId32 = new Random();

       // IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        PingPacket _latestPingSent;
        DateTime? _latestPingSentTime;
        PingPacket _latestPingReceived;// float MaxTxInviteRateRps, MaxTxRegiserRateRps; // sent by remote peer via ping
        
        PongPacket _latestReceivedPong;
        TimeSpan? _latestPingPongDelay;

     //   IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
       // List<TxRegisterRequestState> PendingTxRegisterRequests;
      //  List<TxInviteRequestState> PendingTxInviteRequests;

        DateTime? _lastTimeSentPingRequest;
        DateTime _lastTimeCreatedOrReceivedVerifiedResponsePacket; // is updated when received "alive" signal from remote peer: ping response or ...
        readonly DrpPeerEngine _engine;
        internal DrpPeerEngine Engine => _engine;
        readonly LocalDrpPeer _localDrpPeer;
        internal LocalDrpPeer LocalDrpPeer => _localDrpPeer;
        readonly byte[] LocalEcdhe25519PrivateKey;
        public readonly byte[] LocalEcdhe25519PublicKey;
        bool _disposed;
        public ConnectionToNeighbor(DrpPeerEngine engine, LocalDrpPeer localDrpPeer, ConnectedDrpPeerInitiatedBy initiatedBy)
        {
            _localDrpPeer = localDrpPeer;
            _engine = engine;
            _lastTimeCreatedOrReceivedVerifiedResponsePacket = _engine.DateTimeNowUtc;
            InitiatedBy = initiatedBy;
            P2pConnectionToken32 localRxToken32 = null;
            for (int i = 0; i < 100; i++)
            {
                localRxToken32 = new P2pConnectionToken32 { Token32 = (uint)_engine.InsecureRandom.Next() };
                var rToken16 = localRxToken32.Token16;
                if (_engine.ConnectedPeersByToken16[rToken16] == null)
                {
                    _engine.ConnectedPeersByToken16[rToken16] = this;               
                }
            }
            if (localRxToken32 == null) throw new InsufficientResourcesException();

            LocalRxToken32 = localRxToken32;
            
            _engine.CryptoLibrary.GenerateEcdh25519Keypair(out LocalEcdhe25519PrivateKey, out LocalEcdhe25519PublicKey);
        }
        public void Dispose() // may be called twice
        {
            if (_disposed) return;
            _disposed = true;
            _localDrpPeer.ConnectedPeers.Remove(this);
            _engine.ConnectedPeersByToken16[LocalRxToken32.Token16] = null;
        }

        public PingPacket CreatePing(bool requestRegistrationConfirmationSignature)
        {
            if (_disposed) throw new ObjectDisposedException(_name);
            var r = new PingPacket
            {
                SenderToken32 = RemotePeerToken32,
                MaxRxInviteRateRps = 10, //todo get from some local capabilities   like number of neighbors
                MaxRxRegisterRateRps = 10, //todo get from some local capabilities   like number of neighbors
                PingRequestId32 = (uint)_rngForPingRequestId32.Next(),
            };
            if (requestRegistrationConfirmationSignature)
                r.Flags |= PingPacket.Flags_RegistrationConfirmationSignatureRequested;
            r.SenderHMAC = GetSharedHmac(r.GetSignedFieldsForSenderHMAC);
            return r;
        }
        public void OnTimer100ms(DateTime timeNowUTC, out bool needToRestartLoop) // engine thread
        {
            needToRestartLoop = false;
            if (_disposed) return;
            try
            {
                // remove timed out connected peers (neighbors)
                if (timeNowUTC > _lastTimeCreatedOrReceivedVerifiedResponsePacket + _engine.Configuration.ConnectedPeersRemovalTimeout)
                {
                    _engine.WriteToLog_ping_detail(this, "disposing connection to neighbor: ping response timer has expired");
                    this.Dispose(); // remove dead connected peers (no reply to ping)
                    needToRestartLoop = true;
                    return;
                }

                // send ping requests  when idle (no other "alive" signals from remote peer)
                if (timeNowUTC > _lastTimeCreatedOrReceivedVerifiedResponsePacket + _engine.Configuration.PingRequestsInterval)
                {
                    if (_latestPingPongDelay == null) throw new InvalidOperationException();
                   
                    if (_lastTimeSentPingRequest == null ||
                        timeNowUTC > _lastTimeSentPingRequest.Value.AddSeconds(_latestPingPongDelay.Value.TotalSeconds * _engine.Configuration.PingRetransmissionInterval_RttRatio))
                    {
                        _lastTimeSentPingRequest = timeNowUTC;
                        SendPingRequestOnTimer();
                    }                    
                }
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"error in ConnectedDrpPeer timer procedure: {exc}");
            }
        }
        void SendPingRequestOnTimer()
        {
            var pingRequestPacket = CreatePing(false);
            _engine.SendPacket(pingRequestPacket.Encode(), RemoteEndpoint);
            _latestPingSent = pingRequestPacket;
            _latestPingSentTime = _engine.DateTimeNowUtc;
        }
        void SendPacket(byte[] udpPayload)
        {
            _engine.SendPacket(udpPayload, RemoteEndpoint);
        }
        public void OnReceivedVerifiedPong(PongPacket pong, DateTime responseReceivedAtUTC, TimeSpan? requestResponseDelay)
        {
            _lastTimeCreatedOrReceivedVerifiedResponsePacket = responseReceivedAtUTC;
            _latestReceivedPong = pong;
            _engine.WriteToLog_ping_detail(this, "verified pong");
          
            if (requestResponseDelay.HasValue)
                OnMeasuredRequestResponseDelay(requestResponseDelay.Value);
        }

        void OnMeasuredRequestResponseDelay(TimeSpan requestResponseDelay)
        {
            _engine.WriteToLog_ping_detail(this, $"measured RTT: {(int)requestResponseDelay.TotalMilliseconds}ms");
            _latestPingPongDelay = requestResponseDelay;
        }
        internal void OnReceivedPong(IPEndPoint remoteEndpoint, byte[] udpPayloadData, DateTime receivedAtUtc) // engine thread
        {
            if (_disposed) return;
            if (remoteEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pong = PongPacket.DecodeAndVerify(_engine.CryptoLibrary, udpPayloadData, null, this, false);
                OnReceivedVerifiedPong(pong, receivedAtUtc, null);

                if (_latestPingSent?.PingRequestId32 == pong.PingRequestId32)
                {
                    //  we have multiple previously sent ping requests; and 1 most recent one
                    //  if pong.PingRequestId32   matches to most recently sent ping request ->   update RTT stats
                    OnMeasuredRequestResponseDelay(receivedAtUtc - _latestPingSentTime.Value);
                }
                // else it is out-of-sequence ping response
            }
            catch (PossibleMitmException exc)
            {
                _engine.HandleGeneralException($"breaking P2P connection {this} on MITM exception: {exc}");
                this.Dispose();
            }
        }
        internal void OnReceivedPing(IPEndPoint remoteEndpoint, byte[] udpPayloadData) // engine thread
        {
            if (_disposed) return;
            if (remoteEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pingRequestPacket = PingPacket.DecodeAndVerify(udpPayloadData, this);
                _latestPingReceived = pingRequestPacket;
                var pong = new PongPacket
                {
                    PingRequestId32 = pingRequestPacket.PingRequestId32,
                    SenderToken32 = RemotePeerToken32,
                };
                if ((pingRequestPacket.Flags & PingPacket.Flags_RegistrationConfirmationSignatureRequested) != 0)
                {
                    pong.ResponderRegistrationConfirmationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary,
                        GetResponderRegistrationConfirmationSignatureFields, _localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);                
                }
                pong.SenderHMAC = GetSharedHmac(pong.GetSignedFieldsForSenderHMAC);
              //  _engine.WriteToLog_ping_detail($"sending ping response with senderHMAC={pong.SenderHMAC}");
                _engine.SendPacket(pong.Encode(), RemoteEndpoint);
            }
            catch (PossibleMitmException exc)
            {
                _engine.HandleGeneralException($"breaking P2P connection on MITM exception: {exc}");
                this.Dispose();
            }
        }
        internal void GetResponderRegistrationConfirmationSignatureFields(BinaryWriter w)
        {
            RegisterConfirmationPacket.GetResponderRegistrationConfirmationSignatureFields(w, _syn, _synAck, _ack);
        }
        internal void GetRequesterRegistrationConfirmationSignatureFields(BinaryWriter w, RegistrationSignature responderRegistrationConfirmationSignature)
        {
            RegisterConfirmationPacket.GetRequesterRegistrationConfirmationSignatureFields(w, responderRegistrationConfirmationSignature, _syn, _synAck, _ack);
        }
    }

    /// <summary>
    /// is generated by remote peer; token of local peer at remote peer;
    /// put into every p2p packet,
    /// is needed  1) for faster lookup of remote peer by first 16 of 32 bits 2) to have multiple DRP peer reg IDs running at same UDP port
    /// is unique at remote (responder) peer; is used to identify local (sender) peer at remote peer (together with HMAC)
    /// </summary>
    public class P2pConnectionToken32
    {
        public uint Token32;
        public ushort Token16 => (ushort)(Token32 & 0x0000FFFF);
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Token32);
        }
        public static P2pConnectionToken32 Decode(BinaryReader reader)
        {
            var r = new P2pConnectionToken32();
            r.Token32 = reader.ReadUInt32();
            return r;
        }
        public override bool Equals(object obj)
        {
            return ((P2pConnectionToken32)obj).Token32 == this.Token32;
        }
        public override string ToString() => Token32.ToString();
    }
       
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
}
