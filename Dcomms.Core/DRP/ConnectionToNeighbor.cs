using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// connected or connecting peer, conenction to neighbor peer
    /// 
    /// 
    /// parameters to transmit DRP pings and proxied packets between registered neighbors:
    /// from local peer to remote peer (txParamaters)
    /// from remote peer to local peer (rxParamaters)
    /// is negotiated via REGISTER channel
    /// all fields are encrypted when transmitted over REGISTER channel, using single-block AES and shared ECDH key
    /// 
    /// 
    /// </summary>
    public class ConnectionToNeighbor: IDisposable
    {
        #region tx parameters (parameters to transmit direct (p2p) packets from local peer to neighbor)

        public P2pConnectionToken32 RemotePeerToken32;
        public IPEndPoint RemoteEndpoint; // IP address + UDP port // where to send packets
        public byte[] SharedDhSecret;

        /// <summary>
        /// initializes parameters to transmit direct (p2p) packets form requester A to neighbor N
        /// </summary>
        public void DecryptAtRegisterRequester(RegisterSynPacket localRegisterSyn, RegisterSynAckPacket remoteRegisterSynAck)
        {
            if ((remoteRegisterSynAck.Flags & RegisterSynAckPacket.Flag_ipv6) != 0) throw new NotImplementedException();
            
            SharedDhSecret = _engine.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhe25519PrivateKey, remoteRegisterSynAck.NeighborEcdhePublicKey.ecdh25519PublicKey);

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                localRegisterSyn.GetCommonRequesterAndResponderFields(writer, true);
                remoteRegisterSynAck.GetCommonRequesterAndResponderFields(writer, false, false);
            }
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray());

            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
            var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

            var toNeighborTxParametersDecrypted = new byte[remoteRegisterSynAck.ToNeighborTxParametersEncrypted.Length];
            _engine.CryptoLibrary.ProcessSingleAesBlock(false, aesKey, iv, remoteRegisterSynAck.ToNeighborTxParametersEncrypted, toNeighborTxParametersDecrypted);

            // parse toNeighborTxParametersDecrypted
            using (var reader = new BinaryReader(new MemoryStream(toNeighborTxParametersDecrypted)))
            {
                RemoteEndpoint = PacketProcedures.DecodeIPEndPointIpv4(reader);
                RemotePeerToken32 = P2pConnectionToken32.Decode(reader);
                var magic16 = reader.ReadUInt16();
                if (magic16 != Magic16_ipv4_responderToRequester) throw new BrokenCipherException();
            }
        }
        const ushort Magic16_ipv4_responderToRequester = 0x60C1; // is used to validate decrypted data
        
        /// <summary>
        /// when sending SYN-ACK
        /// </summary>
        public byte[] EncryptAtRegisterResponder(RegisterSynPacket remoteRegisterSyn, RegisterSynAckPacket localRegisterSynAck)
        {
            if (remoteRegisterSyn.AtoRP == false) throw new NotImplementedException();
            if (remoteRegisterSyn.RpEndpoint.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new NotImplementedException();

            SharedDhSecret = _engine.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhe25519PrivateKey, remoteRegisterSyn.RequesterEcdhePublicKey.ecdh25519PublicKey);

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                remoteRegisterSyn.GetCommonRequesterAndResponderFields(writer, true);
                localRegisterSynAck.GetCommonRequesterAndResponderFields(writer, false, false);
            }
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray());

            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
            var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

            // encode localRxParameters
            PacketProcedures.CreateBinaryWriter(out var msRxParameters, out var wRxParameters);
            PacketProcedures.EncodeIPEndPointIpv4(wRxParameters, remoteRegisterSyn.RpEndpoint); // 6
            LocalRxToken32.Encode(wRxParameters); // 4
            wRxParameters.Write(Magic16_ipv4_responderToRequester);    // 2
            wRxParameters.Write(_engine.CryptoLibrary.GetRandomBytes(4));      // 4

            var localRxParametersDecrypted = msRxParameters.ToArray(); // total 16 bytes
            var localRxParametersEncrypted = new byte[localRxParametersDecrypted.Length];
            _engine.CryptoLibrary.ProcessSingleAesBlock(true, aesKey, iv, localRxParametersDecrypted, localRxParametersEncrypted);

            return localRxParametersEncrypted;
        }
               
        /// <summary>initializes parameters to transmit direct (p2p) packets form neighbor N to requester A</returns>
        public void DecryptAtRegisterResponder(RegisterSynPacket remoteRegisterSyn, RegisterSynAckPacket localRegisterSynAck, RegisterAckPacket remoteRegisterAck)
        {
            if ((remoteRegisterAck.Flags & RegisterSynAckPacket.Flag_ipv6) != 0) throw new NotImplementedException();
            
            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                remoteRegisterSyn.GetCommonRequesterAndResponderFields(writer, true);
                localRegisterSynAck.GetCommonRequesterAndResponderFields(writer, true, true);
                remoteRegisterAck.GetCommonRequesterAndResponderFields(writer, false, false);
            }
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray());

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
                if (magic16 != Magic16_ipv4_responderToRequester) throw new BrokenCipherException();
            }

            InitializeNeighborTxRxStreams(remoteRegisterSyn, localRegisterSynAck, remoteRegisterAck);
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
                localRegisterSyn.GetCommonRequesterAndResponderFields(writer, true);
                remoteRegisterSynAck.GetCommonRequesterAndResponderFields(writer, true, true);
                localRegisterAckPacket.GetCommonRequesterAndResponderFields(writer, false, false);
            }
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray());

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
        const ushort Magic16_ipv4_requesterToResponder = 0xBFA4; // is used to validate decrypted data



        //   IAuthenticatedEncryptor Encryptor;
        //   IAuthenticatedDecryptor Decryptor;
        byte[] SharedAuthKeyForHMAC; //  this key is shared secret, known only at requester (A) and neighbor (N), it is used for HMAC

        /// <summary>
        /// initializes SharedAuthKeyForHMAC
        /// </summary>
        public void InitializeNeighborTxRxStreams(RegisterSynPacket registerSynPacket, RegisterSynAckPacket registerSynAckPacket, RegisterAckPacket registerAckPacket)
        {
            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                registerSynPacket.GetCommonRequesterAndResponderFields(writer, true);
                registerSynAckPacket.GetCommonRequesterAndResponderFields(writer, true, true);
                registerAckPacket.GetCommonRequesterAndResponderFields(writer, false, true);
            }
            //  var iv = cryptoLibrary.GetHashSHA256(ms.ToArray()); // todo use for encryption

            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);

            SharedAuthKeyForHMAC = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

            //Encryptor = cryptoLibrary.CreateAesEncyptor(iv, aesKey);
            //Decryptor = cryptoLibrary.CreateAesDecyptor(iv, aesKey);
        }
        public HMAC GetSharedHmac(byte[] data)
        {
            if (SharedAuthKeyForHMAC == null) throw new InvalidOperationException();
            return new HMAC
            {
                hmacSha256 = _engine.CryptoLibrary.GetSha256HMAC(SharedAuthKeyForHMAC, data)
            };

        }
        public HMAC GetSharedHmac(Action<BinaryWriter> data)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            data(w);
            return GetSharedHmac(ms.ToArray());
        }
        #endregion



        public ConnectedDrpPeerInitiatedBy InitiatedBy;
      //  public P2pStreamParameters TxParameters;
        public RegistrationPublicKey RemotePeerPublicKey;
        public readonly P2pConnectionToken32 LocalRxToken32; // is generated by local peer
        /// <summary>
        /// ip address and port of local peer, which _can_ be accessible by remote peers via internet
        /// </summary>
        public IPEndPoint LocalEndpoint;
        
      //  ConnectedDrpPeerRating Rating;
        readonly Random _rngForPingRequestId32 = new Random();

       // IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        PingRequestPacket _latestPingRequestPacketSent;
        PingRequestPacket _latestPingRequestPacketReceived;// float MaxTxInviteRateRps, MaxTxRegiserRateRps; // sent by remote peer via ping
        DateTime? _latestPingRequestPacketSentTime;
        
        PingResponsePacket _latestReceivedPingResponsePacket;
        TimeSpan? _latestRequestResponseDelay;

     //   IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
       // List<TxRegisterRequestState> PendingTxRegisterRequests;
      //  List<TxInviteRequestState> PendingTxInviteRequests;

        DateTime? _lastTimeSentPingRequest;
        DateTime _lastTimeCreatedOrReceivedVerifiedResponsePacket; // is updated when received "alive" signal from remote peer: ping response or ...
        readonly DrpPeerEngine _engine;
        internal DrpPeerEngine Engine => _engine;
        readonly LocalDrpPeer _localDrpPeer;
        readonly byte[] LocalEcdhe25519PrivateKey;
        public readonly byte[] LocalEcdhe25519PublicKey;
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
        public void Dispose()
        {
            _localDrpPeer.ConnectedPeers.Remove(this);
            _engine.ConnectedPeersByToken16[LocalRxToken32.Token16] = null;
        }

        public PingRequestPacket CreatePingRequestPacket(bool requestP2pConnectionSetupSignature)
        {
            var r = new PingRequestPacket
            {
                SenderToken32 = RemotePeerToken32,
                MaxRxInviteRateRps = 10, //todo get from some local capabilities   like number of neighbors
                MaxRxRegisterRateRps = 10, //todo get from some local capabilities   like number of neighbors
                PingRequestId32 = (uint)_rngForPingRequestId32.Next(),
            };
            if (requestP2pConnectionSetupSignature)
                r.Flags |= PingRequestPacket.Flags_P2pConnectionSetupSignatureRequested;
            r.SenderHMAC = GetSharedHmac(r.GetSignedFieldsForSenderHMAC);
            return r;
        }
        public void OnTimer100ms(DateTime timeNowUTC, out bool needToRestartLoop) // engine thread
        {
            needToRestartLoop = false;
            try
            {
                // remove timed out connected peers (neighbors)
                if (timeNowUTC > _lastTimeCreatedOrReceivedVerifiedResponsePacket + _engine.Configuration.ConnectedPeersRemovalTimeout)
                {
                    this.Dispose(); // remove dead connected peers (no reply to ping)
                    needToRestartLoop = true;
                    return;
                }

                // send ping requests  when idle (no other "alive" signals from remote peer)
                if (timeNowUTC > _lastTimeCreatedOrReceivedVerifiedResponsePacket + _engine.Configuration.PingRequestsInterval)
                {
                    if (_lastTimeSentPingRequest == null ||
                        timeNowUTC > _lastTimeSentPingRequest.Value.AddSeconds(_latestRequestResponseDelay.Value.TotalSeconds * _engine.Configuration.PingRetransmissionInterval_RttRatio))
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
            var pingRequestPacket = CreatePingRequestPacket(false);
            _engine.SendPacket(pingRequestPacket.Encode(), RemoteEndpoint);
            _latestPingRequestPacketSent = pingRequestPacket;
            _latestPingRequestPacketSentTime = _engine.DateTimeNowUtc;
        }
        void SendPacket(byte[] udpPayload)
        {
            _engine.SendPacket(udpPayload, RemoteEndpoint);
        }
        public void OnReceivedVerifiedPingResponse(PingResponsePacket pingResponsePacket, DateTime responseReceivedAtUTC, TimeSpan? requestResponseDelay)
        {
            _lastTimeCreatedOrReceivedVerifiedResponsePacket = responseReceivedAtUTC;
            _latestReceivedPingResponsePacket = pingResponsePacket;
            // todo process requestResponseDelay to measure RTT and use it for rating
            if (requestResponseDelay.HasValue)
                OnMeasuredRequestResponseDelay(requestResponseDelay.Value);
        }

        void OnMeasuredRequestResponseDelay(TimeSpan requestResponseDelay)
        {
            _latestRequestResponseDelay = requestResponseDelay;
        }

        internal void OnReceivedPingResponsePacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData, DateTime receivedAtUtc) // engine thread
        {
            if (remoteEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pingResponsePacket = PingResponsePacket.DecodeAndVerify(_engine.CryptoLibrary, udpPayloadData, null, this, false, null, null);
                OnReceivedVerifiedPingResponse(pingResponsePacket, receivedAtUtc, null);

                if (_latestPingRequestPacketSent?.PingRequestId32 == pingResponsePacket.PingRequestId32)
                {
                    //  we have multiple previously sent ping requests; and 1 most recent one
                    //  if pingResponse.PingRequestId32   matches to most recently sent ping request ->   update RTT stats
                    OnMeasuredRequestResponseDelay(receivedAtUtc - _latestPingRequestPacketSentTime.Value);
                }
                // else it is out-of-sequence ping response
            }
            catch (PossibleMitmException exc)
            {
                _engine.HandleGeneralException($"breaking p2p connection on MITM exception: {exc}");
                this.Dispose();
            }
        }

        internal void OnReceivedPingRequestPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData) // engine thread
        {
            if (remoteEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pingRequestPacket = PingRequestPacket.DecodeAndVerify(udpPayloadData, this);
                _latestPingRequestPacketReceived = pingRequestPacket;
                var pingResponsePacket = new PingResponsePacket
                {
                    PingRequestId32 = pingRequestPacket.PingRequestId32,
                    SenderToken32 = RemotePeerToken32,
                };
                if ((pingRequestPacket.Flags & PingRequestPacket.Flags_P2pConnectionSetupSignatureRequested) != 0)
                {
                    //   pingResponsePacket.P2pConnectionSetupSignature = xxx;
                    // todo implement it for N: pass reg.syn packets into here
                    throw new NotImplementedException();
                }
                pingResponsePacket.SenderHMAC = GetSharedHmac(pingResponsePacket.GetSignedFieldsForSenderHMAC);            
                _engine.SendPacket(pingResponsePacket.Encode(), RemoteEndpoint);
            }
            catch (PossibleMitmException exc)
            {
                _engine.HandleGeneralException($"breaking P2P connection on MITM exception: {exc}");
                this.Dispose();
            }
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
    }



    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
}
