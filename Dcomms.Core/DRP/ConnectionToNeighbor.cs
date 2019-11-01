using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.DSP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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
    public partial class ConnectionToNeighbor: IDisposable, IVisibleModule, IVisiblePeer
    {
        internal byte[] SharedAuthKeyForNeighborHMAC; //  this key is shared secret, known only at requester (A) and neighbor (N), it is used for HMAC
        RegisterRequestPacket _req;
        RegisterAck1Packet _ack1;
        RegisterAck2Packet _ack2;

        #region tx parameters (parameters to transmit direct (p2p) packets from local peer to neighbor)
        public NeighborToken32 RemoteNeighborToken32;
        public IPEndPoint RemoteEndpoint; // IP address + UDP port // where to send packets
        internal byte[] SharedDhSecret;

        /// <summary>
        /// initializes parameters to transmit direct (p2p) packets form requester A to neighbor N
        /// </summary>
        public void Decrypt_ack1_ToResponderTxParametersEncrypted_AtRequester_DeriveSharedDhSecret(RegisterRequestPacket req, RegisterAck1Packet ack1)
        {            
            SharedDhSecret = _engine.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhe25519PrivateKey, ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

            #region iv, key
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            req.GetSharedSignedFields(writer, true);
            ack1.GetSharedSignedFields(writer, false, false);
           
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();

            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
            var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion


            var toNeighborTxParametersDecrypted = new byte[ack1.ToResponderTxParametersEncrypted.Length];
            _engine.CryptoLibrary.ProcessAesCbcBlocks(false, aesKey, iv, ack1.ToResponderTxParametersEncrypted, toNeighborTxParametersDecrypted);

            // parse toNeighborTxParametersDecrypted
            using (var reader = new BinaryReader(new MemoryStream(toNeighborTxParametersDecrypted)))
            {
                RemoteEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
                RemoteNeighborToken32 = NeighborToken32.Decode(reader);
                var magic16 = reader.ReadUInt16();
                if (magic16 != Magic16_responderToRequester) throw new BrokenCipherException();
            }
            
            _engine.WriteToLog_reg_requesterSide_detail($"decrypted remote responder endpoint={RemoteEndpoint}, remoteNeighborToken={RemoteNeighborToken32} from ACK1", req, LocalDrpPeer);
            
        }
        const ushort Magic16_responderToRequester = 0x60C1; // is used to validate decrypted data
        
        /// <summary>
        /// when sending ACK1
        /// </summary>
        public byte[] Encrypt_ack1_ToResponderTxParametersEncrypted_AtResponder_DeriveSharedDhSecret(RegisterRequestPacket req, RegisterAck1Packet ack1, ConnectionToNeighbor neighbor)
        {
            IPEndPoint localResponderEndpoint;
            if (neighbor != null)
            {
                localResponderEndpoint = neighbor.LocalEndpoint;
            }
            else
            {
                localResponderEndpoint = req.EpEndpoint;
            }

            if (localResponderEndpoint.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new NotImplementedException();

            SharedDhSecret = _engine.CryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhe25519PrivateKey, req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

            #region key, iv
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            req.GetSharedSignedFields(writer, true);
            ack1.GetSharedSignedFields(writer, false, false);

            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();;
            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);

            var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion

            // encode localRxParameters
            PacketProcedures.CreateBinaryWriter(out var msRxParameters, out var wRxParameters);
            PacketProcedures.EncodeIPEndPoint(wRxParameters, localResponderEndpoint); // max 19
            LocalNeighborToken32.Encode(wRxParameters); // +4   max 23

            _engine.WriteToLog_reg_responderSide_detail($"encrypting local responder endpoint={localResponderEndpoint}, localNeighborToken={LocalNeighborToken32} into ACK1", req, this.LocalDrpPeer);

            wRxParameters.Write(Magic16_responderToRequester);    // +2 max 25
            var bytesRemaining = RegisterAck1Packet.ToResponderTxParametersEncryptedLength - (int)msRxParameters.Length;

            wRxParameters.Write(_engine.CryptoLibrary.GetRandomBytes(bytesRemaining));   

            var localRxParametersDecrypted = msRxParameters.ToArray(); // total 16 bytes
            var localRxParametersEncrypted = new byte[localRxParametersDecrypted.Length];
            _engine.CryptoLibrary.ProcessAesCbcBlocks(true, aesKey, iv, localRxParametersDecrypted, localRxParametersEncrypted);

            if (localRxParametersEncrypted.Length != RegisterAck1Packet.ToResponderTxParametersEncryptedLength)
                throw new Exception();
            return localRxParametersEncrypted;          

        }
               
        /// <summary>initializes parameters to transmit direct (p2p) packets form neighbor N to requester A</returns>
        public void Decrypt_ack2_ToRequesterTxParametersEncrypted_AtResponder_InitializeP2pStream(RegisterRequestPacket req, RegisterAck1Packet ack1, RegisterAck2Packet ack2)
        {
            #region key, iv
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
           
            req.GetSharedSignedFields(writer, true);
            ack1.GetSharedSignedFields(writer, true, true);
            ack2.GetSharedSignedFields(writer, false, false);
            
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();

            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
            var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion 

            var toRequesterTxParametersDecrypted = new byte[ack2.ToRequesterTxParametersEncrypted.Length];
            _engine.CryptoLibrary.ProcessAesCbcBlocks(false, aesKey, iv, ack2.ToRequesterTxParametersEncrypted, toRequesterTxParametersDecrypted);

            // parse toRequesterTxParametersDecrypted
            using (var reader = new BinaryReader(new MemoryStream(toRequesterTxParametersDecrypted)))
            {
                RemoteEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
                RemoteNeighborToken32 = NeighborToken32.Decode(reader);
                var magic16 = reader.ReadUInt16();
                if (magic16 != Magic16_ipv4_requesterToResponder) throw new BrokenCipherException();
            }

            _engine.WriteToLog_reg_responderSide_detail($"decrypted remote requester endpoint={RemoteEndpoint}, remoteNeighborToken={RemoteNeighborToken32} from ACK2", req, this.LocalDrpPeer);

            InitializeP2pStream(req, ack1, ack2);            
        }

        /// <summary>
        /// when sending ACK
        /// </summary>
        public byte[] Encrypt_ack2_ToRequesterTxParametersEncrypted_AtRequester(RegisterRequestPacket req, RegisterAck1Packet ack1, RegisterAck2Packet ack2)
        {
            if (SharedDhSecret == null)
                throw new InvalidOperationException();

            #region aes key, iv
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);         
            req.GetSharedSignedFields(writer, true);
            ack1.GetSharedSignedFields(writer, true, true);
            ack2.GetSharedSignedFields(writer, false, false);
           
            var iv = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()).Take(16).ToArray();

            ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);
            var aesKey = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion

            // encode localRxParameters
            PacketProcedures.CreateBinaryWriter(out var msRxParameters, out var wRxParameters);
            PacketProcedures.EncodeIPEndPoint(wRxParameters, LocalEndpoint); // max 19
            LocalNeighborToken32.Encode(wRxParameters); // +4 max 23
            _engine.WriteToLog_reg_requesterSide_detail($"encrypting local requester endpoint={LocalEndpoint}, localNeighborToken={LocalNeighborToken32} into ACK2", req, LocalDrpPeer);
            wRxParameters.Write(Magic16_ipv4_requesterToResponder); // +2 max 25
            var bytesRemaining = RegisterAck2Packet.ToRequesterTxParametersEncryptedLength - (int)msRxParameters.Length;
            wRxParameters.Write(_engine.CryptoLibrary.GetRandomBytes(bytesRemaining));      

            var localRxParametersDecrypted = msRxParameters.ToArray();
            var localRxParametersEncrypted = new byte[localRxParametersDecrypted.Length];
            _engine.CryptoLibrary.ProcessAesCbcBlocks(true, aesKey, iv, localRxParametersDecrypted, localRxParametersEncrypted);

            if (localRxParametersEncrypted.Length != RegisterAck2Packet.ToRequesterTxParametersEncryptedLength) throw new Exception();
            return localRxParametersEncrypted;            
        }
        const ushort Magic16_ipv4_requesterToResponder = 0xBFA4; // is used to validate decrypted data
               
        //   IAuthenticatedEncryptor Encryptor;
        //   IAuthenticatedDecryptor Decryptor;

        /// <summary>
        /// initializes SharedAuthKeyForHMAC
        /// </summary>
        public void InitializeP2pStream(RegisterRequestPacket req, RegisterAck1Packet ack1, RegisterAck2Packet ack2)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());

            _req = req;
            _ack1 = ack1;
            _ack2 = ack2;

            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                req.GetSharedSignedFields(writer, true);
                ack1.GetSharedSignedFields(writer, true, true);
                ack2.GetSharedSignedFields(writer, false, true);           
                //  var iv = cryptoLibrary.GetHashSHA256(ms.ToArray()); // todo use for p2p  encryption

                ms.Write(SharedDhSecret, 0, SharedDhSecret.Length);

                SharedAuthKeyForNeighborHMAC = _engine.CryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

                _engine.WriteToLog_p2p_detail(this, $"initialized P2P stream: SharedAuthKeyForHMAC={MiscProcedures.ByteArrayToString(SharedAuthKeyForNeighborHMAC)}", req);
                //Encryptor = cryptoLibrary.CreateAesEncyptor(iv, aesKey);
                //Decryptor = cryptoLibrary.CreateAesDecyptor(iv, aesKey);
            }
        }
        public void AssertIsNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
        }
        public HMAC GetNeighborHMAC(byte[] data)
        {
            AssertIsNotDisposed();
            if (SharedAuthKeyForNeighborHMAC == null) throw new InvalidOperationException();
            var r = new HMAC
            {
                hmacSha256 = _engine.CryptoLibrary.GetSha256HMAC(SharedAuthKeyForNeighborHMAC, data)
            };
            
          //  Engine.WriteToLog_ping_detail($"<< GetSharedHmac(input={MiscProcedures.ByteArrayToString(data)}, sha256={MiscProcedures.ByteArrayToString(_engine.CryptoLibrary.GetHashSHA256(data))}) returns {r}. SharedAuthKeyForHMAC={MiscProcedures.ByteArrayToString(SharedAuthKeyForHMAC)}");
            return r;
        }
        public HMAC GetNeighborHMAC(Action<BinaryWriter> data)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            data(w);
            return GetNeighborHMAC(ms.ToArray());
        }
        #endregion
               
        public ConnectedDrpPeerInitiatedBy InitiatedBy;

        RegistrationId _remoteRegistrationId;
        public RegistrationId RemoteRegistrationId
        {
            get => _remoteRegistrationId;
            set
            {
                _remoteRegistrationId = value;
                if (_remoteRegistrationId != null)
                {
                    var localPeerVector = _localDrpPeer.LocalPeerRegistrationIdVectorValues;
                    var neighborPeerVector = RegistrationIdDistance.GetVectorValues(_engine.CryptoLibrary, _remoteRegistrationId, _engine.NumberOfDimensions);
                    var vectorFromLocalToNeighbor = new float[localPeerVector.Length];
                    for (int i = 0; i < neighborPeerVector.Length; i++)
                    {
                        var from = localPeerVector[i];
                        var to = neighborPeerVector[i];
                        RegistrationIdDistance.ProcessVectorInLoopedRegistrationIdSpace(from, ref to);   
                        vectorFromLocalToNeighbor[i] = (float)(to - from);
                    }

                    var sectorIndex = Engine.VSIC.GetSectorIndex(vectorFromLocalToNeighbor);
                    SectorIndexFlagsMask = (ushort)(1 << sectorIndex);
                }
            }
        }
        public string RemoteVisionName { get; set; }
        public ushort SectorIndexFlagsMask { get; private set; }
        public override string ToString() => $"connTo{RemoteVisionName}-{RemoteEndpoint}-{RemoteRegistrationId}";        
        string IVisibleModule.Status => $"{RemoteVisionName}-localEP={LocalEndpoint}, remoteEP={RemoteEndpoint}, RTT={_latestPingPongDelay_RTT?.TotalMilliseconds}ms, remoteRegID={RemoteRegistrationId},  LocalNeighborToken32={LocalNeighborToken32}, LocalNeighborToken16={LocalNeighborToken32?.Token16.ToString("X4")}, RemoteNeighborToken32={RemoteNeighborToken32}, RemoteNeighborToken16={RemoteNeighborToken32?.Token16.ToString("X4")}";

        public readonly NeighborToken32 LocalNeighborToken32; // is generated by local peer
        /// <summary>
        /// ip address and port of local peer, which _can_ be accessible by remote peers via internet
        /// </summary>
        public IPEndPoint LocalEndpoint;
        
        /// <summary>
        /// used for PingRequestId32, for NhaSeq16_P2P
        /// </summary>
        readonly Random _insecureRandom = new Random();
        ushort _seq16Counter_P2P; // accessed only by engine thread
        internal RequestP2pSequenceNumber16 GetNewRequestP2pSeq16_P2P()
        {
            AssertIsNotDisposed();
            return new RequestP2pSequenceNumber16 { Seq16 = _seq16Counter_P2P++ };
        }

        // IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        PingPacket _latestPingSent;
        DateTime? _latestPingSentTime;
        PingPacket _latestPingReceived;// float MaxTxInviteRateRps, MaxTxRegiserRateRps; // sent by remote peer via ping
        public ushort? RemoteNeighborsBusySectorIds => _latestPingReceived?.RequesterNeighborsBusySectorIds;
        public bool PingReceived => _latestPingReceived != null;
        public bool CanBeUsedForNewRequests => PingReceived == true && IsInTeardownState == false;

        PongPacket _latestReceivedPong;
        TimeSpan? _latestPingPongDelay_RTT;

     //   IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
       // List<TxRegisterRequestState> PendingTxRegisterRequests;
      //  List<TxInviteRequestState> PendingTxInviteRequests;

        DateTime? _lastTimeSentPingRequest;
        DateTime _lastTimeP2pInitializedOrReceivedVerifiedResponsePacket; // is updated when received "alive" signal from remote peer: ping response or ...
        internal void OnP2pInitialized()
        {
            _lastTimeP2pInitializedOrReceivedVerifiedResponsePacket = _engine.DateTimeNowUtc;
        }
        readonly DrpPeerEngine _engine;
        internal DrpPeerEngine Engine => _engine;
        readonly LocalDrpPeer _localDrpPeer;
        internal LocalDrpPeer LocalDrpPeer => _localDrpPeer;
        readonly byte[] LocalEcdhe25519PrivateKey;
        public readonly byte[] LocalEcdhe25519PublicKey;
        bool _disposed;
        internal bool IsDisposed => _disposed;
        internal bool IsInTeardownState { get; set; }

        public ConnectionToNeighbor(DrpPeerEngine engine, LocalDrpPeer localDrpPeer, ConnectedDrpPeerInitiatedBy initiatedBy, RegistrationId remoteRegistrationId)
        {
            _seq16Counter_P2P = (ushort)_insecureRandom.Next(ushort.MaxValue);
            _localDrpPeer = localDrpPeer;
            _engine = engine;
            _lastTimeP2pInitializedOrReceivedVerifiedResponsePacket = _engine.DateTimeNowUtc;
            RemoteRegistrationId = remoteRegistrationId;
                       
            InitiatedBy = initiatedBy;
            NeighborToken32 localNeighborToken32 = null;
            for (int i = 0; i < 100; i++)
            {
                localNeighborToken32 = new NeighborToken32 { Token32 = (uint)_engine.InsecureRandom.Next() };
                var rToken16 = localNeighborToken32.Token16;
                if (_engine.ConnectedPeersByToken16[rToken16] == null)
                {
                    _engine.ConnectedPeersByToken16[rToken16] = this;
                    break;
                }
            }
            if (localNeighborToken32 == null) throw new InsufficientResourcesException();

            LocalNeighborToken32 = localNeighborToken32;
            
            _engine.CryptoLibrary.GenerateEcdh25519Keypair(out LocalEcdhe25519PrivateKey, out LocalEcdhe25519PublicKey);
            
            engine.Configuration.VisionChannel?.RegisterVisibleModule(engine.Configuration.VisionChannelSourceId, VisibleModulePath, this);

            engine.WriteToLog_p2p_detail(this, $"created p2p connection: remoteRegistrationId={remoteRegistrationId}, LocalNeighborToken32={LocalNeighborToken32}, LocalNeighborToken16={LocalNeighborToken32.Token16.ToString("X4")}", null);

        }
        string VisibleModulePath => $"{_localDrpPeer}{Vision.VisionChannel.PathSeparator}{this.LocalNeighborToken32}";

        float[] IVisiblePeer.VectorValues => RegistrationIdDistance.GetVectorValues(_localDrpPeer.CryptoLibrary, _remoteRegistrationId, _engine.NumberOfDimensions).Select(x => (float)x).ToArray();
        bool IVisiblePeer.Highlighted => false;
        string IVisiblePeer.Name => null;
        IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => null;
        string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer) => null;

        public void Dispose() // may be called twice
        {
            if (_disposed) return;
            _disposed = true;
            _localDrpPeer.ConnectedNeighbors.Remove(this);
            _engine.Configuration.VisionChannel?.UnregisterVisibleModule(_engine.Configuration.VisionChannelSourceId, VisibleModulePath);

            _engine.WriteToLog_p2p_detail(this, $"disposed connection to neighbor: neighborToken16={LocalNeighborToken32.Token16.ToString("X4")}", null);

            _engine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(10), () =>
            {
                _engine.WriteToLog_p2p_detail(this, $"removing neighborToken16={LocalNeighborToken32.Token16.ToString("X4")} from table", null);
                _engine.ConnectedPeersByToken16[LocalNeighborToken32.Token16] = null;   
            });    
        }

        #region ping pong
        public PingPacket CreatePing(bool requestRegistrationConfirmationSignature, bool connectionTeardownFlag, ushort requesterNeighborsBusySectorIds)
        {
            if (_disposed) throw new ObjectDisposedException(ToString());
            var r = new PingPacket
            {
                NeighborToken32 = RemoteNeighborToken32,
                RequesterNeighborsBusySectorIds = requesterNeighborsBusySectorIds,
                MaxRxInviteRateRps = 10, //todo get from some local capabilities   like number of neighbors
                MaxRxRegisterRateRps = 10, //todo get from some local capabilities   like number of neighbors
                PingRequestId32 = (uint)_insecureRandom.Next(),
                VisionName = _localDrpPeer.Engine.Configuration.VisionChannelSourceId
            };
            if (requestRegistrationConfirmationSignature)
                r.Flags |= PingPacket.Flags_RegistrationConfirmationSignatureRequested;
            if (connectionTeardownFlag)
                r.Flags |= PingPacket.Flags_ConnectionTeardown;
            r.NeighborHMAC = GetNeighborHMAC(r.GetSignedFieldsForNeighborHMAC);
            return r;
        }
        public void OnTimer100ms(DateTime timeNowUTC, out bool needToRestartLoop) // engine thread
        {
            needToRestartLoop = false;
            if (_disposed) return;
            try
            {
                // remove timed out connected peers (neighbors)
                if (timeNowUTC > _lastTimeP2pInitializedOrReceivedVerifiedResponsePacket + _engine.Configuration.ConnectedPeersRemovalTimeout)
                {
                    _engine.WriteToLog_p2p_lightPain(this, "disposing connection to neighbor: ping response timer has expired", null);
                    this.Dispose(); // remove dead connected peers (no reply to ping)
                    needToRestartLoop = true;
                    return;
                }

                // send ping requests  when idle (no other "alive" signals from remote peer)
                if (timeNowUTC > _lastTimeP2pInitializedOrReceivedVerifiedResponsePacket + _engine.Configuration.PingRequestsInterval)
                {
                  //  if (_latestPingPongDelay_RTT == null) throw new InvalidOperationException("_latestPingPongDelay = null");
                   
                    if (_lastTimeSentPingRequest == null ||
                        timeNowUTC > _lastTimeSentPingRequest.Value.AddSeconds(((_latestPingPongDelay_RTT ?? TimeSpan.FromSeconds(0.5)).TotalSeconds) * _engine.Configuration.PingRetransmissionInterval_RttRatio))
                    {
                        _lastTimeSentPingRequest = timeNowUTC;
                        SendPingRequestOnTimer();
                    }                    
                }
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"error in ConnectedDrpPeer {this} timer procedure", exc);
            }
        }
        void SendPingRequestOnTimer()
        {
            var pingRequestPacket = CreatePing(false, false, _localDrpPeer.ConnectedNeighborsBusySectorIds);
            SendPacket(pingRequestPacket.Encode());
            _latestPingSent = pingRequestPacket;
            _latestPingSentTime = _engine.DateTimeNowUtc;
        }
        internal void OnReceivedVerifiedPong(PongPacket pong, DateTime responseReceivedAtUTC, TimeSpan? requestResponseDelay)
        {
            _lastTimeP2pInitializedOrReceivedVerifiedResponsePacket = responseReceivedAtUTC;
            _latestReceivedPong = pong;
            _engine.WriteToLog_p2p_detail(this, "verified pong", null);
          
            if (requestResponseDelay.HasValue)
                OnMeasuredRequestResponseDelay(requestResponseDelay.Value);
        }
        void OnMeasuredRequestResponseDelay(TimeSpan requestResponseDelay)
        {
            _engine.WriteToLog_p2p_detail(this, $"measured RTT: {(int)requestResponseDelay.TotalMilliseconds}ms", null);
            _latestPingPongDelay_RTT = requestResponseDelay;
        }
        internal void OnReceivedPong(IPEndPoint remoteEndpoint, byte[] udpData, DateTime receivedAtUtc) // engine thread
        {
            _engine.WriteToLog_p2p_detail(this, "received PONG", null);
            if (_disposed) return;
            if (remoteEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pong = PongPacket.DecodeAndVerify(_engine.CryptoLibrary, udpData, null, this, false);
                OnReceivedVerifiedPong(pong, receivedAtUtc, null);

                if (_latestPingSent?.PingRequestId32 == pong.PingRequestId32)
                {
                    //  we have multiple previously sent ping requests; and 1 most recent one
                    //  if pong.PingRequestId32   matches to most recently sent ping request ->   update RTT stats
                    OnMeasuredRequestResponseDelay(receivedAtUtc - _latestPingSentTime.Value);
                }
                // else it is out-of-sequence ping response
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"Exception while processing PONG in {this}", exc); // dont dispose the connection to avoid DoS'es.   if HMAC is not good - we ignore the bad packet
            }
        }
        internal void OnReceivedPing(IPEndPoint remoteEndpoint, byte[] udpData) // engine thread
        {
            _engine.WriteToLog_p2p_detail(this, "received PING", null);
            if (_disposed)
            {
                _engine.WriteToLog_p2p_detail(this, "ignoring PING: connection is disposed", null);
                return;
            }
            if (this.RemoteEndpoint == null)
            {
                _engine.WriteToLog_p2p_detail(this, "ignoring PING: correct remote endpoint is unknown yet", null);
                return;
            }
            if (remoteEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.WriteToLog_p2p_lightPain(this, $"ignoring PING: bad source endpoint {remoteEndpoint}, correct is {this.RemoteEndpoint}", null);
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pingRequestPacket = PingPacket.DecodeAndVerify(udpData, this);
                _latestPingReceived = pingRequestPacket;
                if (pingRequestPacket.VisionName != null) RemoteVisionName = pingRequestPacket.VisionName;
                var pong = new PongPacket
                {
                    PingRequestId32 = pingRequestPacket.PingRequestId32,
                    NeighborToken32 = RemoteNeighborToken32,
                };
                if ((pingRequestPacket.Flags & PingPacket.Flags_RegistrationConfirmationSignatureRequested) != 0)
                {
                    pong.ResponderRegistrationConfirmationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary,
                        GetResponderRegistrationConfirmationSignatureFields, _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey);                
                }
                pong.NeighborHMAC = GetNeighborHMAC(pong.GetSignedFieldsForNeighborHMAC);
              //  _engine.WriteToLog_ping_detail($" sending ping response with senderHMAC={pong.NeighborHMAC}");
                SendPacket(pong.Encode());

                if ((pingRequestPacket.Flags & PingPacket.Flags_ConnectionTeardown) != 0 && !IsInTeardownState)
                {
                    IsInTeardownState = true;
                    _engine.WriteToLog_p2p_higherLevelDetail(this, "received PING with connection teardown flag set: destroying P2P connection", null);
                    Engine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(PingPacket.ConnectionTeardownStateDurationS), () =>
                    {
                        if (!IsDisposed)
                        {
                            _engine.WriteToLog_p2p_higherLevelDetail(this, "destroying P2P connection after teardown state timeout", null);
                            this.Dispose();
                        }
                    });
                }
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"Exception while processing PING in {this}", exc); // dont dispose the connection to avoid DoS'es.   if HMAC is not good - we ignore the bad packet
            }
        }
        #endregion

        internal async Task OnReceivedRegisterReq(IPEndPoint requesterEndpoint, byte[] udpData, DateTime reqReceivedTimeUtc)
        {
            if (_disposed) return;
            if (requesterEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(requesterEndpoint);
                return;
            }
            try
            {
                // we got REQ from this instance neighbor
                var req = RegisterRequestPacket.Decode_OptionallyVerifyNeighborHMAC(udpData, this);
                // NeighborToken32 and NeighborHMAC are verified at this time
                
                var receivedRequest = new ReceivedRequest(LocalDrpPeer, this, requesterEndpoint, reqReceivedTimeUtc) { RegisterReq = req };
                _engine.WriteToLog_p2p_higherLevelDetail(this, $"received {req} ({req.NumberOfHopsRemaining} hops remaining) via P2P connection", req);
                
                if (req.RequesterRegistrationId.Equals(this.LocalDrpPeer.Configuration.LocalPeerRegistrationId))
                    _engine.WriteToLog_routing_lightPain($"received {req} to same reg. ID", req, LocalDrpPeer);

                await _engine.ProcessRegisterRequestAsync(LocalDrpPeer, receivedRequest);
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"Exception while processing REGISTER REQ in {this}", exc); // dont dispose the connection to avoid DoS'es.   if HMAC is not good - we ignore the bad packet
            }
        }

        internal async Task OnReceivedInviteReq(IPEndPoint requesterEndpoint, byte[] udpData, DateTime reqReceivedTimeUtc)
        {
            if (_disposed) return;
            if (requesterEndpoint.Equals(this.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(requesterEndpoint);
                return;
            }
            try
            {
                // we got REQ from this instance neighbor
                var req = InviteRequestPacket.Decode_VerifyNeighborHMAC(udpData, this);
                Engine.WriteToLog_inv_detail($"{this} received {req}", req, LocalDrpPeer);
                // NeighborToken32 and NeighborHMAC are verified at this time

                if (!_engine.ValidateReceivedReqTimestamp32S(req.ReqTimestamp32S))
                    throw new BadSignatureException();

                var receivedRequest = new ReceivedRequest(LocalDrpPeer, this,  requesterEndpoint, reqReceivedTimeUtc) { InviteReq = req };
                if (LocalDrpPeer.PendingInviteRequestExists(req.RequesterRegistrationId))
                {
                    Engine.WriteToLog_inv_lightPain($"rejecting {req}: another INVITE request is already being processed", req, LocalDrpPeer);
                    await receivedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                    return;
                }

                if (req.ResponderRegistrationId.Equals(this.LocalDrpPeer.Configuration.LocalPeerRegistrationId))
                {
                    _ = this.LocalDrpPeer.AcceptInviteRequestAsync(receivedRequest);
                }
                else
                {
 _retry:
                    var destinationPeer = _engine.RouteInviteRequest(this.LocalDrpPeer, receivedRequest); // routing
                    if (destinationPeer == null)
                    {
                        // no neighbors found. send error response to sender
                        await receivedRequest.SendErrorResponse(ResponseOrFailureCode.failure_routeIsUnavailable);
                        return;
                    }

                    var needToRerouteToAnotherNeighbor = await this.LocalDrpPeer.ProxyInviteRequestAsync(receivedRequest, destinationPeer);            
                    if (needToRerouteToAnotherNeighbor)
                    {
                        receivedRequest.TriedNeighbors.Add(destinationPeer);
                        Engine.WriteToLog_inv_proxySide_detail($"retrying to proxy invite to another neighbor on error. already tried {receivedRequest.TriedNeighbors.Count}", req, LocalDrpPeer);             
                        goto _retry;
                    }

                }
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"Exception while processing INVITE REQ in {this}", exc); // dont dispose the connection to avoid DoS'es.   if HMAC is not good - we ignore the bad packet
            }
        }

        internal void SendPacket(byte[] udpPayload)
        {
            _engine.SendPacket(udpPayload, RemoteEndpoint);
        }
        internal async Task SendUdpRequestAsync_Retransmit_WaitForNPACK(byte[] requestUdpData, RequestP2pSequenceNumber16 reqP2pSeq16, 
            Action<BinaryWriter> npaRequestFieldsForNeighborHmacNullable = null)
        {
            await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(requestUdpData, RemoteEndpoint, reqP2pSeq16, this, npaRequestFieldsForNeighborHmacNullable);
        }
               
        internal void GetResponderRegistrationConfirmationSignatureFields(BinaryWriter w)
        {
            RegisterConfirmationPacket.GetResponderRegistrationConfirmationSignatureFields(w, _req, _ack1, _ack2);
        }
        internal void GetRequesterRegistrationConfirmationSignatureFields(BinaryWriter w, RegistrationSignature responderRegistrationConfirmationSignature)
        {
            RegisterConfirmationPacket.GetRequesterRegistrationConfirmationSignatureFields(w, responderRegistrationConfirmationSignature, _req, _ack1, _ack2);
        }
        
    }

    /// <summary>
    /// is generated by remote peer; token of local peer at remote peer;
    /// put into every p2p packet,
    /// is needed  1) for faster lookup of remote peer by first 16 of 32 bits 2) to have multiple DRP peer reg IDs running at same UDP port
    /// is unique at remote (responder) peer; is used to identify local (sender) peer at remote peer (together with HMAC)
    /// </summary>
    public class NeighborToken32
    {
        public uint Token32;
        public ushort Token16 => (ushort)(Token32 & 0x0000FFFF);
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Token32);
        }
        public static NeighborToken32 Decode(BinaryReader reader)
        {
            var r = new NeighborToken32();
            r.Token32 = reader.ReadUInt32();
            return r;
        }
        public override bool Equals(object obj)
        {
            return ((NeighborToken32)obj).Token32 == this.Token32;
        }
        public override string ToString() => Token32.ToString("X8");
    }
       
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
}
