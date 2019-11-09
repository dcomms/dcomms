using Dcomms.DRP.Packets;
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
    /// <summary>
    /// handles PoW1 request
    /// handles regSyn with PoW2
    /// </summary>
    partial class DrpPeerEngine
    {
        bool Pow1IsOK(RegisterPow1RequestPacket packet, byte[] clientPublicIP)
        {
            var ms = new MemoryStream(sizeof(uint) + packet.ProofOfWork1.Length + clientPublicIP.Length);
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(packet.Timestamp32S);
                writer.Write(packet.ProofOfWork1);
                writer.Write(clientPublicIP);
                ms.Position = 0;
                var hash = _cryptoLibrary.GetHashSHA512(ms);

                if (hash[4] != 7 || (hash[5] != 7 && hash[5] != 8)) return false;
                else return true;
            }
        }
        bool Pow2IsOK(RegisterRequestPacket packet, byte[] proofOrWork2Request)
        {
            var ms = new MemoryStream(packet.RequesterRegistrationId.Ed25519publicKey.Length + proofOrWork2Request.Length + packet.ProofOfWork2.Length);
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(packet.RequesterRegistrationId.Ed25519publicKey);
                writer.Write(proofOrWork2Request);
                writer.Write(packet.ProofOfWork2);
                ms.Position = 0;

                var hash = _cryptoLibrary.GetHashSHA512(ms);
                if (hash[4] != 7 || (hash[5] != 7 && hash[5] != 8)) return false;
                else return true;
            }
        }

        UniqueDataFilter16MbRAM _recentUniquePow1Data;
        Pow2RequestsTable _pow2RequestsTable;
        partial void Initialize(DrpPeerEngineConfiguration config)
        {
             if (config.SandboxModeOnly_DisablePoW == false)
                _recentUniquePow1Data = new UniqueDataFilter16MbRAM(Timestamp32S, config.RegisterPow1_RecentUniqueDataResetPeriodS);
             _pow2RequestsTable = new Pow2RequestsTable(config);
        }
       
        /// <summary>
        /// is executed by receiver thread
        /// </summary>
        void ProcessRegisterPow1RequestPacket(IPEndPoint requesterEndpoint, byte[] udpData)
        {
            var packet = new RegisterPow1RequestPacket(udpData);
            if (!PassPow1filter(requesterEndpoint, packet))
            {
                if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide) <= AttentionLevel.needsAttention)
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide, AttentionLevel.needsAttention, $"pow1 filter rejected request from {requesterEndpoint}");
                return;
            }

            // create Pow2 request state
            var pow2RequestState = _pow2RequestsTable.GenerateOrGetExistingPow2(requesterEndpoint);

            var response = new RegisterPow1ResponsePacket
            {  
                ProofOfWork2Request = pow2RequestState.ProofOfWork2Request,
                StatusCode = RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge,
                Pow1RequestId = packet.Pow1RequestId
            };
            SendPacket(response.Encode(), requesterEndpoint);
        }

        /// <summary>
        /// sends responses 
        /// executed by receiver thread
        /// </summary>
        bool PassPow1filter(IPEndPoint requesterEndpoint, RegisterPow1RequestPacket packet)
        {
            // verify size of Pow1 data
            if (packet.ProofOfWork1.Length != 64)
            {
                if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide) <= AttentionLevel.needsAttention)
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide, AttentionLevel.needsAttention, $"pow1 filter rejected request from {requesterEndpoint}: invalid pow1 length");
                return false;
            }

            var localTimeSec32 = Timestamp32S;
            var timeDifferenceSec = Math.Abs((int)unchecked(localTimeSec32 - packet.Timestamp32S));
            if (timeDifferenceSec > Configuration.RegisterPow1_MaxTimeDifferenceS)
            {
                if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide) <= AttentionLevel.needsAttention)
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide, AttentionLevel.needsAttention, $"pow1 filter rejected request from {requesterEndpoint}: invalid timestamp");

                // respond with error "try again with valid clock" - legitimate user has to get valid clock from some time server and synchronize itself with the server
                if (Configuration.RespondToRegisterPow1Errors) RespondToRegisterPow1withError(requesterEndpoint, RegisterPow1ResponseStatusCode.rejected_badtimestamp, packet.Pow1RequestId);
                return false;
            }

            if (!Pow1IsOK(packet, requesterEndpoint.Address.GetAddressBytes()))
            {
                if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide) <= AttentionLevel.needsAttention)
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide, AttentionLevel.needsAttention, $"pow1 filter rejected request from {requesterEndpoint}: invalid pow1");

                OnReceivedBadRegisterReqPow1(requesterEndpoint);
                // no response
                return false;
            }

            if (_recentUniquePow1Data != null)
            {
                // check if pow1 data is unique
                var dataIsUnique = _recentUniquePow1Data.TryInputData(packet.ProofOfWork1, localTimeSec32);
                if (dataIsUnique)
                {
                    return true;
                }
                else
                {
                    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide) <= AttentionLevel.needsAttention)
                        Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_epSide, AttentionLevel.needsAttention, $"pow1 filter rejected request from {requesterEndpoint}: pow1 data is not unique");

                    // respond with error "try again with unique PoW data"
                    if (Configuration.RespondToRegisterPow1Errors) RespondToRegisterPow1withError(requesterEndpoint, RegisterPow1ResponseStatusCode.rejected_tryagainRightNowWithThisServer, packet.Pow1RequestId);
                    return false;
                }
            }
            return true;
        }
        void RespondToRegisterPow1withError(IPEndPoint requesterEndpoint, RegisterPow1ResponseStatusCode statusCode, uint pow1RequestId)
        {
            var response = new RegisterPow1ResponsePacket
            {
                StatusCode = statusCode,
                Pow1RequestId = pow1RequestId
            };
            SendPacket(response.Encode(), requesterEndpoint);
        }
        
        /// <summary>
        /// is executed by receiver thread
        /// </summary>
        void ProcessRegisterReqAtoEpPacket(IPEndPoint requesterEndpoint, byte[] udpData, DateTime reqReceivedAtUtc)
        {
            Pow2RequestState pow2RequestState = null;
            if (!Configuration.SandboxModeOnly_DisablePoW)
            {
                pow2RequestState = _pow2RequestsTable.TryGetPow2RequestState(requesterEndpoint);
                if (pow2RequestState == null)
                {
                    OnReceivedRegisterReqAtoEpPacketFromUnknownSource(requesterEndpoint);
                    return;
                }
            }

            var req = RegisterRequestPacket.Decode_OptionallyVerifyNeighborHMAC(udpData, null, Configuration.SandboxModeOnly_NumberOfDimensions);

            if (!Configuration.SandboxModeOnly_DisablePoW)
            {
                if (!Pow2IsOK(req, pow2RequestState.ProofOfWork2Request))
                {
                    OnReceivedRegisterReqAtoEpPacketWithBadPow2(requesterEndpoint);
                    // intentionally we dont respond to requester, in case if it is attack
                    return;
                }
            }
          
            EngineThreadQueue.Enqueue(() =>
            {
                _ = ProcessRegisterReqAtoEpPacket2Async(requesterEndpoint, req, reqReceivedAtUtc);
            }, "ProcessRegisterReqAtoEpPacket2Async436");
        }
        async Task ProcessRegisterReqAtoEpPacket2Async(IPEndPoint requesterEndpoint, RegisterRequestPacket req, DateTime reqReceivedTimeUtc)
        {
            var logger = new Logger(this, LocalPeers.Values.First(), req, VisionChannelModuleName_reg);
            var routedRequest = new RoutedRequest(logger, null, requesterEndpoint, reqReceivedTimeUtc, null, req);
            await ProcessRegisterRequestAsync(null, routedRequest);
        }
    }


    /// <summary>
    /// thread-unsafe
    /// generates snonce0 objects
    /// stores them for "period" = 5 seconds in Dictionary, by client endpoint
    /// max capacity: 100K per second, 5 seconds: 500K*snonce0 =     ...................
    /// </summary>
    class Pow2RequestsTable
    {
        readonly Random _rnd = new Random();
        Dictionary<IPEndPoint, Pow2RequestState> _currentPeriodStates = new Dictionary<IPEndPoint, Pow2RequestState>();
        Dictionary<IPEndPoint, Pow2RequestState> _previousPeriodStates = new Dictionary<IPEndPoint, Pow2RequestState>();

        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        TimeSpan TimeNowRel => _stopwatch.Elapsed;
        TimeSpan _nextPeriodSwitchTimeRel;
        readonly DrpPeerEngineConfiguration _config;
        public Pow2RequestsTable(DrpPeerEngineConfiguration config)
        {
            _config = config;
            _nextPeriodSwitchTimeRel = config.Pow2RequestStatesTablePeriod;
        }
        /// <summary>
        /// generates new pow2request object
        /// resets state when necessary
        /// </summary>
        public Pow2RequestState GenerateOrGetExistingPow2(IPEndPoint requesterEndpoint)
        {
            var timeNowRel = TimeNowRel;

            if (timeNowRel > _nextPeriodSwitchTimeRel || _currentPeriodStates.Count > _config.Pow2RequestStatesTableMaxSize)
            { // switch tables
                _previousPeriodStates = _currentPeriodStates;
                _currentPeriodStates = new Dictionary<IPEndPoint, Pow2RequestState>();
                _nextPeriodSwitchTimeRel = timeNowRel + _config.Pow2RequestStatesTablePeriod;
            }

            var existingPow2RequestState = TryGetPow2RequestState(requesterEndpoint);
            if (existingPow2RequestState != null) return existingPow2RequestState;

            var r = new Pow2RequestState
            {
                ProofOfWork2Request = new byte[16]
            };
            _rnd.NextBytes(r.ProofOfWork2Request);
            _currentPeriodStates.Add(requesterEndpoint, r);
            return r;
        }
        public Pow2RequestState TryGetPow2RequestState(IPEndPoint requesterEndpoint)
        {
            if (_currentPeriodStates.TryGetValue(requesterEndpoint, out var r))
                return r;
            if (_previousPeriodStates.TryGetValue(requesterEndpoint, out r))
                return r;
            return null;
        }
    }


    class Pow2RequestState
    {
        public byte[] ProofOfWork2Request; // 16 bytes
    }
}
