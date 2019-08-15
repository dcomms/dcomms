using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

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
            }
            var hash = _cryptoLibrary.GetHashSHA512(ms);
            if (hash[4] != 7 || (hash[5] != 7 && hash[5] != 8)
                //     || hash[6] > 100
                )
                return false;
            else return true;
        }
        bool Pow2IsOK(RegisterSynPacket packet, byte[] proofOrWork2Request)
        {
            var ms = new MemoryStream(packet.RequesterPublicKey_RequestID.ed25519publicKey.Length + proofOrWork2Request.Length + packet.ProofOfWork2.Length);
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(packet.RequesterPublicKey_RequestID.ed25519publicKey);
                writer.Write(proofOrWork2Request);
                writer.Write(packet.ProofOfWork2);
            }
            var hash = _cryptoLibrary.GetHashSHA512(ms);
            if (hash[4] != 7 || (hash[5] != 7 && hash[5] != 8)
                //     || hash[6] > 100
                )
                return false;
            else return true;
        }

        UniqueDataTracker _recentUniquePow1Data;
        Pow2RequestsTable _pow2RequestsTable;
        partial void Initialize(DrpPeerEngineConfiguration config)
        {
             _recentUniquePow1Data = new UniqueDataTracker(Timestamp32S, config.RegisterPow1_RecentUniqueDataResetPeriodS);
             _pow2RequestsTable = new Pow2RequestsTable(config);
        }
       
        /// <summary>
        /// is executed by receiver thread
        /// </summary>
        void ProcessRegisterPow1RequestPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData)
        {
            var packet = new RegisterPow1RequestPacket(udpPayloadData);
            if (!PassPow1filter(remoteEndpoint, packet))
                return;

            // create Pow2 request state
            var pow2RequestState = _pow2RequestsTable.GenerateOrGetExistingPow2(remoteEndpoint);

            var response = new RegisterPow1ResponsePacket
            {  
                ProofOfWork2Request = pow2RequestState.ProofOfWork2Request,
                StatusCode = RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge,
                Pow1RequestId = packet.Pow1RequestId
            };
            SendPacket(response.Encode(), remoteEndpoint);
        }

        /// <summary>
        /// sends responses 
        /// executed by receiver thread
        /// </summary>
        bool PassPow1filter(IPEndPoint remoteEndpoint, RegisterPow1RequestPacket packet)
        { 
            // verify size of Pow1 data
            if (packet.ProofOfWork1.Length != 64)
                return false;

            var localTimeSec32 = Timestamp32S;
            var timeDifferenceSec = Math.Abs((int)unchecked(localTimeSec32 - packet.Timestamp32S));
            if (timeDifferenceSec > Configuration.RegisterPow1_MaxTimeDifferenceS)
            {
                // respond with error "try again with valid clock" - legitimate user has to get valid clock from some time server and synchronize itself with the server
                if (Configuration.RespondToRegisterPow1Errors) RespondToRegisterPow1(remoteEndpoint, RegisterPow1ResponseStatusCode.rejected_badtimestamp, packet.Pow1RequestId);
                return false;
            }

            if (!Pow1IsOK(packet, remoteEndpoint.Address.GetAddressBytes()))
            {
                OnReceivedBadRegisterSynPow1(remoteEndpoint);
                // no response
                return false;
            }

            // check if pow1 data is unique
            var dataIsUnique = _recentUniquePow1Data.TryInputData(packet.ProofOfWork1, localTimeSec32);
            if (dataIsUnique)
            {
                return true;
            }
            else
            {
                // respond with error "try again with unique PoW data"
                if (Configuration.RespondToRegisterPow1Errors) RespondToRegisterPow1(remoteEndpoint, RegisterPow1ResponseStatusCode.rejected_tryagainRightNowWithThisServer, packet.Pow1RequestId);
                return false;
            }             
        }
        void RespondToRegisterPow1(IPEndPoint remoteEndpoint, RegisterPow1ResponseStatusCode statusCode, uint pow1RequestId)
        {
            var response = new RegisterPow1ResponsePacket
            {
                StatusCode = statusCode,
                Pow1RequestId = pow1RequestId
            };
            SendPacket(response.Encode(), remoteEndpoint);
        }
        
        /// <summary>
        /// is executed by receiver thread
        /// </summary>
        void ProcessRegisterSynAtoRpPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData)
        {  
            var pow2RequestState = _pow2RequestsTable.TryGetPow2RequestState(remoteEndpoint);
            if (pow2RequestState == null)
            {
                OnReceivedRegisterSynAtoRpPacketFromUnknownSource(remoteEndpoint);
                return;
            }

            var registerSynPacket = new RegisterSynPacket(udpPayloadData);

            if (!Pow2IsOK(registerSynPacket, pow2RequestState.ProofOfWork2Request))
            {
                OnReceivedRegisterSynAtoRpPacketWithBadPow2(remoteEndpoint);
                // no response
                return;
            }

            // questionable:    hello1IPlimit table:  limit number of requests  per 1 minute from every IPv4 block: max 100? requests per 1 minute from 1 block
            //   ------------ possible attack on hello1IPlimit  table???

            _engineThreadQueue.Enqueue(() =>
            {             
                RouteRegistrationRequest(registerSynPacket, out var proxyTo, out var acceptAt); // routing

                if (acceptAt != null)
                {   // accept the registration request here, at RP
                    TryBeginAcceptRegisterRequest(acceptAt, registerSynPacket, remoteEndpoint);
                }
                else if (proxyTo != null)
                {  // todo proxy
                    throw new NotImplementedException();
                }
                else throw new Exception();
            });
        }
        /// <summary>
        /// main routing procedure for REGISTER requests
        /// </summary>
        void RouteRegistrationRequest(RegisterSynPacket registerSynPacket, out ConnectedDrpPeer proxyTo, out LocalDrpPeer acceptAt)
        {
            proxyTo = null;
            acceptAt = null;
            RegistrationPublicKeyDistance minDistance = null;
            foreach (var localPeer in LocalPeers.Values)
            {
                foreach (var connectedPeer in localPeer.ConnectedPeers)
                {
                    var distanceToConnectedPeer = registerSynPacket.RequesterPublicKey_RequestID.GetDistanceTo(connectedPeer.RemotePeerPublicKey);
                    if (minDistance == null || minDistance.IsGreaterThan(distanceToConnectedPeer))
                    {
                        minDistance = distanceToConnectedPeer;
                        proxyTo = connectedPeer;
                        acceptAt = null;
                    }
                }
                var distanceToLocalPeer = registerSynPacket.RequesterPublicKey_RequestID.GetDistanceTo(localPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey);
                if (minDistance == null || minDistance.IsGreaterThan(distanceToLocalPeer))
                {
                    minDistance = distanceToLocalPeer;
                    proxyTo = null;
                    acceptAt = localPeer;
                }
            }
            if (minDistance == null) throw new Exception();
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
        public Pow2RequestState GenerateOrGetExistingPow2(IPEndPoint remoteEndpoint)
        {
            var timeNowRel = TimeNowRel;

            if (timeNowRel > _nextPeriodSwitchTimeRel || _currentPeriodStates.Count > _config.Pow2RequestStatesTableMaxSize)
            { // switch tables
                _previousPeriodStates = _currentPeriodStates;
                _currentPeriodStates = new Dictionary<IPEndPoint, Pow2RequestState>();
                _nextPeriodSwitchTimeRel = timeNowRel + _config.Pow2RequestStatesTablePeriod;
            }

            var existingSnonce0 = TryGetPow2RequestState(remoteEndpoint);
            if (existingSnonce0 != null) return existingSnonce0;

            var r = new Pow2RequestState
            {
                ProofOfWork2Request = new byte[16]
            };
            _rnd.NextBytes(r.ProofOfWork2Request);
            _currentPeriodStates.Add(remoteEndpoint, r);
            return r;
        }
        public Pow2RequestState TryGetPow2RequestState(IPEndPoint remoteEndpoint)
        {
            if (_currentPeriodStates.TryGetValue(remoteEndpoint, out var r))
                return r;
            if (_previousPeriodStates.TryGetValue(remoteEndpoint, out r))
                return r;
            return null;
        }
    }


    class Pow2RequestState
    {
        public byte[] ProofOfWork2Request; // 16 bytes
    }
}
