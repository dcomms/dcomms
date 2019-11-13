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
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    /// <summary>
    /// an object to be created by user application (messenger, higher-level)
    /// runs one UDP socket
    /// hosts objects/resources/threads
    /// </summary>
    public partial class DrpPeerEngine : IDisposable, IVisibleModule
    {
        readonly ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        internal ICryptoLibrary CryptoLibrary => _cryptoLibrary;
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        TimeSpan TimeSWE => _stopwatch.Elapsed; // stopwatch elapsed
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + TimeSWE; } }
        public uint Timestamp32S => MiscProcedures.DateTimeToUint32seconds(DateTimeNowUtc);
        public Int64 Timestamp64 => MiscProcedures.DateTimeToInt64ticks(DateTimeNowUtc);
        bool _disposing;
        Thread _engineThread;
        Thread _powThread;
        Thread _receiverThread;
        UdpClient _socket;
        internal ActionsQueue EngineThreadQueue;
        ActionsQueue PowThreadQueue;
        public readonly ExecutionTimeStatsCollector ETSC;
        public ExecutionTimeTracker CreateTracker(string actionVisibleId)
        {
            Action<string> wtl = null;
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general) <= AttentionLevel.deepDetail)
            {
                wtl = (msg) =>
                {
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general, AttentionLevel.deepDetail, msg);
                };               
            }


            return  new ExecutionTimeTracker(ETSC, actionVisibleId, wtl);
        }
        readonly Random _insecureRandom;
        internal Random InsecureRandom => _insecureRandom;
        Dictionary<RegistrationId, LocalDrpPeer> LocalPeers = new Dictionary<RegistrationId, LocalDrpPeer>(); // accessed only by engine thread     
        internal IEnumerable<IVisiblePeer> VisibleLocalPeers => LocalPeers.Values;
        internal ConnectionToNeighbor[] ConnectedPeersByToken16 = new ConnectionToNeighbor[ushort.MaxValue+1];
        internal DMP.InviteSession[] InviteSessionsByToken16 = new DMP.InviteSession[ushort.MaxValue + 1];
               
        string IVisibleModule.Status => $"socket: {_socket?.Client?.LocalEndPoint}, local peers: {LocalPeers.Count}, ConnectedPeer tokens: {ConnectedPeersByToken16.Count(x => x != null)}, InviteSession tokens: {InviteSessionsByToken16.Count(x => x != null)}, queue count: {EngineThreadQueue.Count}. delays:\r\n{ETSC.PeakExecutionTimeStats}";
        
        ushort _seq16Counter_AtoEP; // accessed only by engine thread
        internal RequestP2pSequenceNumber16 GetNewNpaSeq16_AtoEP() => new RequestP2pSequenceNumber16 { Seq16 = _seq16Counter_AtoEP++ };
        public DrpPeerEngineConfiguration Configuration { get; private set; }

        #region unique data filters
        /// <summary>
        /// is used to make sure that processed ECDH keys are unique
        /// </summary>
        internal readonly UniqueDataFilter RecentUniquePublicEcdhKeys = new UniqueDataFilter(10000);
        internal readonly UniqueDataFilter RecentUniqueProxiedRegistrationRequests_NonRandomHop = new UniqueDataFilter(1000);
        internal readonly UniqueDataFilter RecentUniqueProxiedRegistrationRequests_RandomHop = new UniqueDataFilter(1000);
        internal readonly UniqueDataFilter RecentUniqueAcceptedRegistrationRequests = new UniqueDataFilter(1000);
        internal readonly UniqueDataFilter RecentUniqueInviteRequests = new UniqueDataFilter(1000);
        #endregion

        public readonly VectorSectorIndexCalculator VSIC;
        public int NumberOfDimensions => Configuration.SandboxModeOnly_NumberOfDimensions;
        public DrpPeerEngine(DrpPeerEngineConfiguration configuration)
        {
            ETSC = new ExecutionTimeStatsCollector(() => configuration.VisionChannel.TimeNow);
            configuration.VisionChannel?.RegisterVisibleModule(configuration.VisionChannelSourceId, "DrpPeerEngine", this);

            _insecureRandom = configuration.InsecureRandomSeed.HasValue ? new Random(configuration.InsecureRandomSeed.Value) : new Random();
            Configuration = configuration;
            VSIC = new VectorSectorIndexCalculator(NumberOfDimensions);
            Initialize(configuration);
            _seq16Counter_AtoEP = (ushort)_insecureRandom.Next(ushort.MaxValue);
            EngineThreadQueue = new ActionsQueue(exc => HandleExceptionInEngineThread(exc), ETSC);
            PowThreadQueue = new ActionsQueue(exc => HandleGeneralException("error in PoW thread:", exc), null);

            _socket = new UdpClient(configuration.LocalPort ?? 0);
            _receiverThread = new Thread(ReceiverThreadEntry);
            _receiverThread.Name = "DRP receiver";
            _receiverThread.Priority = ThreadPriority.Highest;
            _receiverThread.Start();

            _engineThread = new Thread(EngineThreadEntry);
            _engineThread.Name = "DRP engine";
            _engineThread.Start();

            _powThread = new Thread(PowThreadEntry);
            _powThread.Priority = ThreadPriority.Lowest;
            _powThread.Name = "PoW";
            _powThread.Start();
        }
        partial void Initialize(DrpPeerEngineConfiguration configuration);
        public void Dispose()
        {
            if (_disposing) return;
            _disposing = true;
            EngineThreadQueue.Dispose();
            _engineThread.Join();
            PowThreadQueue.Dispose();
            _powThread.Join();
            _socket.Close();
            _socket.Dispose();
            _receiverThread.Join();
        }
        public override string ToString() => Configuration.VisionChannelSourceId;

        #region receiver thread
        void ReceiverThreadEntry()
        {
            IPEndPoint remoteEndpoint = default(IPEndPoint);
            while (!_disposing)
            {
                try
                {
                    var udpData = _socket.Receive(ref remoteEndpoint);
                    ProcessReceivedUdpPacket(remoteEndpoint, udpData);
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
        void ProcessReceivedUdpPacket(IPEndPoint remoteEndpoint, byte[] udpData) // receiver thread
        {
            var packetType = (PacketTypes)udpData[0];
            WriteToLog_receiver_detail($"received packet {packetType} from {remoteEndpoint} ({udpData.Length} bytes, hash={MiscProcedures.GetArrayHashCodeString(udpData)})");
            if (packetType == PacketTypes.RegisterPow1Request)
            {
                ProcessRegisterPow1RequestPacket(remoteEndpoint, udpData);
                return;
            }

            var receivedAtUtc = DateTimeNowUtc;
            if (packetType == PacketTypes.RegisterReq)
            {
                if (RegisterRequestPacket.IsAtoEP(udpData))
                {
                    ProcessRegisterReqAtoEpPacket(remoteEndpoint, udpData, receivedAtUtc);
                    return;
                }
            }

            EngineThreadQueue.Enqueue(() =>
            {
                var delayMs = (DateTimeNowUtc - receivedAtUtc).TotalMilliseconds;
                OnMeasuredEngineThreadQueueDelay(receivedAtUtc, delayMs);
                if (RespondersToRetransmittedRequests_ProcessPacket(remoteEndpoint, udpData)) return;
                if (PendingUdpRequests_ProcessPacket(remoteEndpoint, udpData, receivedAtUtc)) return;

                switch (packetType)
                {
                    case PacketTypes.Ping:
                        {
                            var neighborToken16 = PingPacket.DecodeNeighborToken16(udpData);
                            var connectedPeer = ConnectedPeersByToken16[neighborToken16];
                            WriteToLog_receiver_detail($"got connectedPeer={connectedPeer} by neighborToken16={neighborToken16.ToString("X4")} to process ping udp data {MiscProcedures.ByteArrayToString(udpData)}");
                           
                            if (connectedPeer != null)
                            {
                                if (connectedPeer.IsDisposed)
                                {
                                    WriteToLog_receiver_detail($"connectedPeer={connectedPeer} is disposed, being removed from table");
                                    return;
                                }
                                connectedPeer.OnReceivedPing(remoteEndpoint, udpData);
                            }
                            else
                                WriteToLog_receiver_lightPain($"packet {packetType} from {remoteEndpoint} has invalid NeighborToken={neighborToken16.ToString("X4")}");

                        } break;
                    case PacketTypes.Pong:
                        {
                            var neighborToken16 = PongPacket.DecodeNeighborToken16(udpData);
                            var connectedPeer = ConnectedPeersByToken16[neighborToken16];
                            if (connectedPeer != null)
                            {
                                if (connectedPeer.IsDisposed)
                                {
                                    WriteToLog_receiver_detail($"connectedPeer={connectedPeer} is disposed, being removed from table");
                                    return;
                                }
                                connectedPeer.OnReceivedPong(remoteEndpoint, udpData, receivedAtUtc);
                            }
                            else
                                WriteToLog_receiver_lightPain($"packet {packetType} from {remoteEndpoint} has invalid NeighborToken={neighborToken16.ToString("X4")}");
                        }
                        break;
                    case PacketTypes.RegisterReq:
                        {
                            var neighborToken16 = RegisterRequestPacket.DecodeNeighborToken16(udpData);
                            var connectedPeer = ConnectedPeersByToken16[neighborToken16];
                            if (connectedPeer != null)
                            {
                                if (connectedPeer.IsDisposed)
                                {
                                    WriteToLog_receiver_lightPain($"can't process REGISTER REQ: connectedPeer={connectedPeer} is disposed, being removed from table");
                                    return;
                                }
                                _ = connectedPeer.OnReceivedRegisterReq(remoteEndpoint, udpData, receivedAtUtc);
                            }
                            else
                                WriteToLog_receiver_lightPain($"packet {packetType} from {remoteEndpoint} has invalid NeighborToken={neighborToken16}");
                        }
                        break;
                    case PacketTypes.InviteReq:
                        {
                            var neighborToken16 = InviteRequestPacket.DecodeNeighborToken16(udpData);
                            var connectedPeer = ConnectedPeersByToken16[neighborToken16];
                            if (connectedPeer != null)
                            {
                                if (connectedPeer.IsDisposed)
                                {
                                    WriteToLog_receiver_lightPain($"can't process INVITE REQ: connectedPeer={connectedPeer} is disposed, being removed from table");
                                    return;
                                }
                                _ = connectedPeer.OnReceivedInviteReq(remoteEndpoint, udpData, receivedAtUtc);
                            }
                            else
                                WriteToLog_receiver_lightPain($"packet {packetType} from {remoteEndpoint} has invalid NeighborToken={neighborToken16}");
                        }
                        break;
                    case PacketTypes.DmpPing:
                        {
                            var dcToken16 = DMP.Packets.DmpPingPacket.DecodeDcToken16(udpData);
                            var session = InviteSessionsByToken16[dcToken16];
                            if (session != null)
                                session.OnReceivedDmpPing(remoteEndpoint, udpData);
                            else
                                WriteToLog_receiver_lightPain($"packet {packetType} from {remoteEndpoint} has invalid DcToken={dcToken16}");
                        }
                        break;                  
                }
            }, $"ProcRecv {packetType}");
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
                    EngineThreadQueue.ExecuteQueued();

                    var timeNowUTC = DateTimeNowUtc;
                    if (timeNowUTC > nextTimeToCallOnTimer100ms)
                    {
                        nextTimeToCallOnTimer100ms = nextTimeToCallOnTimer100ms.AddMilliseconds(100);
                        EngineThreadOnTimer100ms(timeNowUTC);
                    }
                }
                catch (Exception exc)
                {
                    HandleExceptionInEngineThread( exc);
                }
                Thread.Sleep(10);
            }
        }
        void EngineThreadOnTimer100ms(DateTime timeNowUTC) // engine thread 
        {
            // for every connected peer
            foreach (var localPeer in LocalPeers.Values)
            {
              _restart_loop:
                foreach (var connectedPeer in localPeer.ConnectedNeighbors)
                {
                    connectedPeer.OnTimer100ms(timeNowUTC, out var needToRestartLoop);
                    if (needToRestartLoop)
                        goto _restart_loop;                   
                }


                localPeer.EngineThreadOnTimer100ms(timeNowUTC);
            }
            //   update IIR counters for rates
            //   send ping in case of inactivity
            //   retransmit packets


            // PendingAcceptedRegisterRequests_OnTimer100ms(timeNowUTC);

            // retransmit lowlevel udp requests
            PendingUdpRequests_OnTimer100ms(timeNowUTC);

            // remove expired responders
            RespondersToRetransmittedRequests_OnTimer100ms(timeNowUTC);
        }
        #endregion


        void PowThreadEntry()
        {
            while (!_disposing)
            {
                try
                {
                    PowThreadQueue.ExecuteQueued();
                }
                catch (Exception exc)
                {
                    HandleGeneralException("error in PoW thread: ", exc);
                }
                Thread.Sleep(10);
            }
        }

        internal bool ValidateReceivedReqTimestamp32S(uint receivedReqTimestamp32S)
        {
            var differenceS = Math.Abs((int)receivedReqTimestamp32S - Timestamp32S);
            return differenceS < Configuration.MaxReqTimestampDifferenceS;
        }
        internal bool ValidateReceivedReqTimestamp64(Int64 receivedReqTimestamp64)
        {
            var differenceTicks64 = Math.Abs(receivedReqTimestamp64 - Timestamp64);
            return MiscProcedures.Int64ToTimeSpan(differenceTicks64) < TimeSpan.FromSeconds(Configuration.MaxReqTimestampDifferenceS);
        }
    }

    public interface IDrpRegisteredPeerApp 
    {
        void OnReceivedShortSingleMessage(string messageText);
        /// <summary>
        /// searches for a known user in local contact book
        /// </summary>
        void OnReceivedInvite(RegistrationId remoteRegistrationId, out DMP.UserId remoteUserId, out DMP.UserCertificate localUserCertificateWithPrivateKey, out bool autoReceiveShortSingleMessage);
    }
}
