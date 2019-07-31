using Dcomms.Cryptography;
using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    public class DrpPeerEngine : IDisposable
    {
        static ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        TimeSpan TimeSWE => _stopwatch.Elapsed; // stopwatch elapsed
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + TimeSWE; } }
        uint Timestamp32S => MiscProcedures.DateTimeToUint32(DateTimeNowUtc);
        bool _disposing;
        Thread _engineThread;
        Thread _receiverThread;
        UdpClient _socket;
        ActionsQueue _engineThreadQueue;
        Dictionary<RegistrationPublicKey, LocalDrpPeer> LocalPeers;

        public DrpPeerEngine(DrpPeerEngineConfiguration configuration)
        {
            _engineThreadQueue = new ActionsQueue(exc => HandleExceptionInEngineThread(exc));

            _socket = new UdpClient(configuration.LocalPort ?? 0);
            _receiverThread = new Thread(ReceiverThreadEntry);
            _receiverThread.Name = "DRP receiver";
            _receiverThread.Priority = ThreadPriority.Highest;
            _receiverThread.Start();

            _engineThread = new Thread(EngineThreadEntry);
            _engineThread.Name = "DRP engine";
            _engineThread.Start();
        }
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;
            _engineThreadQueue.Dispose();
            _engineThread.Join();
            _socket.Close();
            _socket.Dispose();
            _receiverThread.Join();
        }

        #region error handlers / dev vision
        void HandleException(Exception exc, string description)
        {
            //todo report to log/dev vision
        }
        void HandleExceptionInReceiverThread(Exception exc)
        {
            //todo report to log/dev vision
        }
        void HandleExceptionInEngineThread(Exception exc)
        {
            //todo report to log/dev vision
        }
        void WriteToLog_reg_requesterSide_debug(string message)
        {

        }
        void HandleExceptionWhileConnectingToRP(IPEndPoint rpEndpoint, Exception exc)
        {
            HandleException(exc, $"exception while connecting to RP {rpEndpoint}");
            // todo: analyse if it is malformed packet received from attacker's RP
        }
        #endregion

        #region registration, for requester peer (A): conenct to the p2P network via rendezvous peer (RP)
        /// <summary>
        /// returns control only when LocalDrpPeer is registered and ready for operation ("local user logged in")
        /// </summary>
        public async Task<LocalDrpPeer> RegisterAsync(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            await _engineThreadQueue.EnqueueAsync();
            
            var localDrpPeer = new LocalDrpPeer(registrationConfiguration, user);            
            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationPublicKey, localDrpPeer);

            if (registrationConfiguration.RendezvousPeerEndpoints.Length != 0)
            {
                WriteToLog_reg_requesterSide_debug($"resolving local public IP...");
                var localPublicIp = await SendPublicApiRequestAsync("http://api.ipify.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicApiRequestAsync("http://ip.seeip.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicApiRequestAsync("http://bot.whatismyipaddress.com");
                if (localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");
                localDrpPeer.LocalPublicIpAddressForRegistration = new IPAddress(localPublicIp);
                WriteToLog_reg_requesterSide_debug($"resolved local public IP = {localDrpPeer.LocalPublicIpAddressForRegistration}");

                await _engineThreadQueue.EnqueueAsync();
                foreach (var rpEndpoint in registrationConfiguration.RendezvousPeerEndpoints) // try to connect to rendezvous peers, one by one
                {
                    if (MiscProcedures.EqualByteArrays(rpEndpoint.Address.GetAddressBytes(), localPublicIp) == true)
                    {
                        WriteToLog_reg_requesterSide_debug($"not connecting to RP {rpEndpoint}: same IP address as local public IP");
                    }
                    else
                    {
                        try
                        {
                            if (await RegisterAsync(localDrpPeer, rpEndpoint) == false)
                                continue;

                            //  on error or timeout try next rendezvous server
                        }
                        catch (Exception exc)
                        {
                            HandleExceptionWhileConnectingToRP(rpEndpoint, exc);
                        }
                    }
                }
            } 
            else
            {
                WriteToLog_reg_requesterSide_debug($"resolving local public IP...");
            }

            return localDrpPeer;
        }

        /// <returns>false if registration failed with timeout or some error code</returns>
        public async Task<bool> RegisterAsync(LocalDrpPeer localDrpPeer, IPEndPoint rpEndpoint)
        {
            WriteToLog_reg_requesterSide_debug($"connecting to RP {rpEndpoint}...");

            #region PoW1
            var registerPow1RequestPacketData = GenerateRegisterPow1RequestPacket(localDrpPeer.LocalPublicIpAddressForRegistration.GetAddressBytes(), Timestamp32S);

            // send register pow1 request
            var rpPow1ResponsePacketData = await SendUdpRequestAsync(
                        new PendingLowLevelUdpRequest(rpEndpoint,
                            new byte[] { (byte)DrpPacketType.RegisterPow1ResponsePacket },
                            registerPow1RequestPacketData,
                            DateTimeNowUtc
                        ));
            //  wait for response, retransmit
            if (rpPow1ResponsePacketData == null)
            {
                WriteToLog_reg_requesterSide_debug($"... connection to RP {rpEndpoint} timed out");
                return false;
            }

            var pow1ResponsePacket = new RegisterPow1ResponsePacket(PacketProcedures.CreateBinaryReader(rpPow1ResponsePacketData, 1));
            if (pow1ResponsePacket.StatusCode != RegisterPowResponseStatusCode.succeeded_Pow2Challenge)
            {
                WriteToLog_reg_requesterSide_debug($"... connection to RP {rpEndpoint} failed with status {pow1ResponsePacket.StatusCode}");
                // error: go to next RP
                return false;
            }
            #endregion

            #region register SYN
            // calculate PoW2
            var registerSynPacket = new RegisterSynPacket
            {
                RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                Timestamp32S = Timestamp32S,
                MinimalDistanceToNeighbor = 0,
                NumberOfHopsRemaining = 10,
                SenderToken16 = new RemotePeerToken16()
            };
            GenerateRegisterSynPow2(registerSynPacket, pow1ResponsePacket.ProofOfWork2Request);
            registerSynPacket.RequesterSignature = new RegistrationSignature
            {
                ed25519signature = _cryptoLibrary.SignEd25519(
                    PacketProcedures.JoinFields(registerSynPacket.RequesterPublicKey_RequestID.ed25519publicKey, registerSynPacket.Timestamp32S, registerSynPacket.MinimalDistanceToNeighbor),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey.ed25519privateKey)
            };
            var registerSynPacketData = registerSynPacket.Encode(RegisterSynPacket.Flag_AtoRP);

            var registerSynPacket_NextHopResponsePacketData = await SendUdpRequestAsync(
                        new PendingLowLevelUdpRequest(rpEndpoint,
                            new byte[] { (byte)DrpPacketType.NextHopResponsePacket },
                            registerSynPacketData,
                            DateTimeNowUtc
                        )); // wait for "DrpNextHopResponsePacket" response from RP

            if (registerSynPacket_NextHopResponsePacketData == null)
            {
                WriteToLog_reg_requesterSide_debug($"... connection to RP {rpEndpoint} timed out (DrpNextHopResponsePacket)");
                return false;
            }
            var registerSynPacket_NextHopResponse = new NextHopResponsePacket(PacketProcedures.CreateBinaryReader(registerSynPacket_NextHopResponsePacketData, 1));
            if (registerSynPacket_NextHopResponse.StatusCode != NextHopResponseCode.received)
            {
                WriteToLog_reg_requesterSide_debug($"... connection to RP {rpEndpoint} failed with status {registerSynPacket_NextHopResponse.StatusCode} (DrpNextHopResponsePacket)");
                return false;
            }
            #endregion

            // todo: wait for "RegisterSynAckPacket"
            var registerSynAckPacketData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(rpEndpoint,
                            new byte[] { (byte)DrpPacketType.RegisterSynAckPacket },
                            null,
                            DateTimeNowUtc,
                            TimeSpan.FromSeconds(10)
                        ));
            if (registerSynAckPacketData == null)
            {
                WriteToLog_reg_requesterSide_debug($"...connection to neighbor via RP {rpEndpoint} timed out (RegisterSynAckPacket)");
                return false;
            }
            var registerSynAckPacket = new RegisterSynAckPacket(PacketProcedures.CreateBinaryReader(registerSynAckPacketData, 1));
            // verify signature of N


            //    connect to neighbor N, retransmit, get signed confirmation from N
            
            // await   
            
            // send registration confirmed packet
            
            return true;
        }
        /// <returns>bytes of IP address</returns>
        async Task<byte[]> SendPublicApiRequestAsync(string url)
        {
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                var response = await httpClient.GetAsync(url);
                var result = await response.Content.ReadAsStringAsync();
                var ipAddress = IPAddress.Parse(result);
                return ipAddress.GetAddressBytes();
            }
            catch (Exception exc)
            {
                HandleException(exc, $"public api request to {url} failed");
                return null;
            }
        }        
        /// <summary>
        /// performs PoW#1 (stateless proof of work)
        /// </summary>
        static byte[] GenerateRegisterPow1RequestPacket(byte[] clientPublicIp, uint timeSec32UTC)
        {
            var packet = new RegisterPow1RequestPacket();
            packet.Timestamp32S = timeSec32UTC;
            packet.ProofOfWork1 = new byte[64];
          
            var rnd = new Random();
            for (; ; )
            {
                rnd.NextBytes(packet.ProofOfWork1);
                if (Pow1IsOK(packet, clientPublicIp)) break;
            }

            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            packet.Encode(writer);
            var packetData = ms.ToArray();
            return packetData;
        }

        static void GenerateRegisterSynPow2(RegisterSynPacket packet, byte[] proofOfWork2Request)
        {
            packet.ProofOfWork2 = new byte[64];
            var rnd = new Random();
            for (; ; )
            {
                rnd.NextBytes(packet.ProofOfWork2);
                if (Pow2IsOK(packet, proofOfWork2Request)) break;
            }
        }

        #endregion
        #region registration RP-side
        static bool Pow1IsOK(RegisterPow1RequestPacket packet, byte[] clientPublicIP)
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
        static bool Pow2IsOK(RegisterSynPacket packet, byte[] proofOrWork2Request)
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
        #endregion

        #region receiver thread
        void ReceiverThreadEntry()
        {
            IPEndPoint remoteEndpoint = default(IPEndPoint);
            while (!_disposing)
            {
                try
                {
                    var udpPayloadData = _socket.Receive(ref remoteEndpoint);
                    ProcessReceivedUdpPacket(remoteEndpoint, udpPayloadData);
                }
                catch (SocketException exc)
                {
                    if (_disposing) return;
                    if (exc.ErrorCode != 10054) // forcibly closed - ICMP port unreachable - it is normal when peer gets down
                        HandleExceptionInReceiverThread(exc);
                    // else ignore it
                }
                catch (Exception exc)
                {
                    HandleExceptionInReceiverThread(exc);
                }
            }
        }
        void ProcessReceivedUdpPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData) // receiver thread
        {
            var packetType = (DrpPacketType)udpPayloadData[0];

            if (packetType == DrpPacketType.RegisterPow1RequestPacket)
            {
                // if packet from new peer (register syn)
                //    process register pow  like in ccp
                return;
            }


            _engineThreadQueue.Enqueue(() =>
            {
                // process responses to  low-level UDP requests
                if (PendingUdpRequests_ProcessPacket(remoteEndpoint, udpPayloadData))
                    return;

                // if packet is from existing connected peer (ping, proxied invite/register)
                //   see which peer sends this packet by streamID, authentcate by source IP:port and  HMAC
                //   update and limit rx packet rate - blacklist regID
                //   process register requests:
                //     see if local peer is good neighbor, reply
                //     decrement nhops, check if it is not 0, proxy to some neighbor:
                //        subroutine create requestViaConnectedPeer
                //   process invite:
                //     see if local peer is destination, pass to user, reply; set up DirectChannel
                //     decrement nhops, check if it is not 0, proxy to some neighbor:
                //        subroutine create requestViaConnectedPeer

                //   process invite/register reponses: pass to original requester, verify responder signature and update rating
                //     when request is complete, clean state

                //   process ping request/response: reply; measure RTT

            });

        }
        #endregion

        #region engine thread
        void EngineThreadEntry()
        {
            DateTime nextTimeToCallOnTimer100ms = DateTimeNowUtc.AddMilliseconds(100);
            while (!_disposing)
            {
                try
                {                
                    _engineThreadQueue.ExecuteQueued();

                    var timeNowUTC = DateTimeNowUtc;
                    if (timeNowUTC > nextTimeToCallOnTimer100ms)
                    {
                        nextTimeToCallOnTimer100ms = nextTimeToCallOnTimer100ms.AddMilliseconds(100);
                        OnTimer100ms(timeNowUTC);
                    }
                }
                catch (Exception exc)
                {
                    HandleExceptionInEngineThread( exc);
                }
                Thread.Sleep(10);
            }
        }
        void OnTimer100ms(DateTime timeNowUTC) // engine thread 
        {
            // for every connected peer
            //   update IIR counters for rates
            //   send ping in case of inactivity
            //   remove dead connected peers (no reply to ping)
            //   retransmit packets
            //   clean timed out requests, raise timeout events

            // expand neighborhood

            // send test packets between local registered peers

            // retransmit lowlevel udp requests
            PendingUdpRequests_OnTimer100ms(timeNowUTC);
        }
        #endregion

        public void BeginSendInvite(RegistrationPublicKey localPeerRegistrationPublicKey, RegistrationPublicKey remotePeerRegistrationPublicKey, byte[] message, Action<DrpResponderStatusCode> callback)
        {
            // find RegisteredLocalDrpPeer

            // find closest neighbor to destination

            // send invite

            // subroutine create requestViaConnectedPeer
        }

        /// <summary>
        /// creates an instance of TxRequestState, starts retransmission timer
        /// </summary>
        bool TrySendRequestViaConnectedPeer(ConnectedDrpPeer connectedPeer, RegistrationPublicKey remotePeerRegistrationPublicKey)
        {
            // assert tx rate is not exceeded  -- return false

            // create an instance of TxRequestState, add it to list

            // send packet to peer

            throw new NotImplementedException();
        }
        #region low-level requests, retransmissions   -- used for registration only
        class PendingLowLevelUdpRequest
        {
            public IPEndPoint RemoteEndpoint;
            public byte[] ResponseFirstBytes;
            public byte[] RequestPacketData; // is null when no need to retransmit request packet during the waiting
            public DateTime ExpirationTimeUTC;
            public DateTime NextRetransmissionTimeUTC;
            public TaskCompletionSource<byte[]> TaskCompletionSource = new TaskCompletionSource<byte[]>();
            TimeSpan CurrentRetransmissionTimeout;
            public PendingLowLevelUdpRequest(IPEndPoint remoteEndpoint, byte[] responseFirstBytes, byte[] requestPacketData, DateTime timeUtc, TimeSpan? expirationTimeout = null)
            {
                ExpirationTimeUTC = timeUtc + (expirationTimeout ?? ExpirationTimeout);
                CurrentRetransmissionTimeout = InitialRetransmissionTimeout;
                NextRetransmissionTimeUTC = timeUtc + InitialRetransmissionTimeout;
                RemoteEndpoint = remoteEndpoint;
                ResponseFirstBytes = responseFirstBytes;
                RequestPacketData = requestPacketData;
            }
            public void OnRetransmitted()
            {
                CurrentRetransmissionTimeout = TimeSpan.FromTicks((long)(CurrentRetransmissionTimeout.Ticks * RetransmissionTimeoutIncrement));
                NextRetransmissionTimeUTC += CurrentRetransmissionTimeout;
            }
            static readonly TimeSpan ExpirationTimeout = TimeSpan.FromSeconds(2);
            static readonly TimeSpan InitialRetransmissionTimeout = TimeSpan.FromSeconds(0.2);
            static readonly float RetransmissionTimeoutIncrement = 1.5f;
        }
        LinkedList<PendingLowLevelUdpRequest> _pendingLowLevelUdpRequests = new LinkedList<PendingLowLevelUdpRequest>(); // accessed by engine thread only
                
        /// <summary>
        /// sends udp packet
        /// expects response from same IPEndpoint, with specified first bytes 
        /// retransmits the packet if no response
        /// returns null on timeout
        /// </summary>
        async Task<byte[]> SendUdpRequestAsync(PendingLowLevelUdpRequest request)
        {
            _socket.Send(request.RequestPacketData, request.RequestPacketData.Length, request.RemoteEndpoint);
            return await WaitForUdpResponseAsync(request);
        }
        async Task<byte[]> WaitForUdpResponseAsync(PendingLowLevelUdpRequest request)
        {
            _pendingLowLevelUdpRequests.AddLast(request);
            return await request.TaskCompletionSource.Task;
        }

        /// <summary>
        /// raises timeout events, retransmits packets
        /// </summary>
        void PendingUdpRequests_OnTimer100ms(DateTime timeNowUTC)
        {
            for (var item = _pendingLowLevelUdpRequests.First; item != null; )
            {
                var request = item.Value;        
                if (timeNowUTC > request.ExpirationTimeUTC)
                {
                    var nextItem = item.Next;
                    _pendingLowLevelUdpRequests.Remove(item);
                    request.TaskCompletionSource.SetResult(null);
                    item = nextItem;
                    continue;
                }
                else if (timeNowUTC > request.NextRetransmissionTimeUTC)
                {
                    request.OnRetransmitted();
                    _socket.Send(request.RequestPacketData, request.RequestPacketData.Length, request.RemoteEndpoint);
                }
                item = item.Next;
            }
        }

        /// <summary>
        /// is executed by engine thread
        /// </summary>
        /// <returns>
        /// reue if the response is linked to request, and the packet is processed
        /// </returns>
        bool PendingUdpRequests_ProcessPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData)
        {
            for (var item  =_pendingLowLevelUdpRequests.First; item != null; item = item.Next)
            {
                var request = item.Value;
                if (request.RemoteEndpoint.Equals(remoteEndpoint) && MiscProcedures.EqualByteArrayHeader(request.ResponseFirstBytes, udpPayloadData))
                {
                    _pendingLowLevelUdpRequests.Remove(item);
                    request.TaskCompletionSource.SetResult(udpPayloadData);
                    return true;
                }
            }
            return false;
        }


        #endregion
    }

    /// <summary>
    /// "contact point" of local user in the regID space
    /// can be "registered" or "registering"
    /// </summary>
    public class LocalDrpPeer
    {
        public IPAddress LocalPublicIpAddressForRegistration;
        LocalDrpPeerState State;
        readonly DrpPeerRegistrationConfiguration _registrationConfiguration;
        public DrpPeerRegistrationConfiguration RegistrationConfiguration => _registrationConfiguration;
        readonly IDrpRegisteredPeerUser _user;
        public LocalDrpPeer(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            _registrationConfiguration = registrationConfiguration;
            _user = user;
        }
        List<ConnectedDrpPeer> ConnectedPeers; // neighbors
        /// <summary>
        /// main routing procedure
        /// selects next peer (hop) to proxy packet
        /// returns null in case of flood
        /// </summary>
        ConnectedDrpPeer TryRouteRequest(RegistrationPublicKey targetRegistrationPublicKey)
        {
            // enumerate conn. peers
            //   skip flooded tx connections (where tx rate is exceeded)
            //   get distance = xor;   combined with ping RTT and rating    (based on RDR)
            //   
            throw new NotImplementedException();
        }
        public void Dispose() // unregisters
        {

        }
    }
    enum LocalDrpPeerState
    {
        requestingPublicIp,
        pow,
        registerSynSent,
        pingEstablished,
        minNeighborsCountAchieved,
        achievedGoodRatingForNeighbors // ready to send requests
    }


    public class DrpPeerEngineConfiguration
    {
        public ushort? LocalPort;
    }
    public class DrpPeerRegistrationConfiguration
    {
        public IPEndPoint[] RendezvousPeerEndpoints; // in case when local peer IP = rendezvous peer IP, it is skipped
        public RegistrationPublicKey LocalPeerRegistrationPublicKey;
        public RegistrationPrivateKey LocalPeerRegistrationPrivateKey;
        public int? NumberOfNeighborsToKeep;
    }

    public interface IDrpRegisteredPeerUser
    {
        void OnReceivedMessage(byte[] message);
    }

    enum DrpPeerConnectionInitiatedBy
    {
        localPeer, // local peer connected to remote peer via REGISTER procedure
        remotePeer // re mote peer connected to local peer via REGISTER procedure
    }
    class ConnectedDrpPeer
    {
        DrpPeerConnectionInitiatedBy InitiatedBy;
        RegistrationPublicKey RemotePeerPublicKey;
        SecretHeyForHmac SecretKeyForHmac;

        ConnectedDrpPeerRating Rating;

        IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        float MaxTxInviteRateRps, MaxTxRegiserRaateRps; // sent by remote peer via ping
        IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
        List<TxRegisterRequestState> PendingTxRegisterRequests;
        List<TxInviteRequestState> PendingTxInviteRequests;
    }
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
    /// <summary>
    /// contains recent RDRs;  RAM-based database
    /// is used to prioritize requests in case of DoS
    /// 50 neighbors, 1 req per second, 1 hour: 180K records: 2.6MB
    /// </summary>
    class RequestDetailsRecordsHistory
    {
        LinkedList<RequestDetailsRecord> Records; // newest first
    }
    class RequestDetailsRecord
    {
        RegistrationPublicKey Sender;
        RegistrationPublicKey Receiver;
        RegistrationPublicKey Requester;
        RegistrationPublicKey Responder;
        DateTime RequestCreatedTimeUTC;
        DateTime RequestFinishedTimeUTC;
        DrpResponderStatusCode Status;
    }
    enum RequestType
    {
        invite,
        register
    }

    class TxRequestState
    {
        DateTime WhenCreatedUTC; // is taken from local clock
        DateTime LatestTxUTC; // is taken from local clock
        int TxPacketsCount; // is incremented when UDP packet is retransmitted

        ConnectedDrpPeer ReceivedFrom;
        ConnectedDrpPeer ProxiedTo;
    }

    class TxRegisterRequestState: TxRequestState
    {
        RegistrationPublicKey RequesterPublicKey_RequestID;
    }

    class TxInviteRequestState: TxRequestState
    {
        // requestID={RequesterPublicKey|DestinationResponderPublicKey}
        RegistrationPublicKey RequesterPublicKey; // A public key 
        RegistrationPublicKey DestinationResponderPublicKey; // B public key
    }
}
