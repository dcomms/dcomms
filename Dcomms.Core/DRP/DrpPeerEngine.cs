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
    public partial class DrpPeerEngine : IDisposable
    {
        readonly ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        internal ICryptoLibrary CryptoLibrary => _cryptoLibrary;
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        TimeSpan TimeSWE => _stopwatch.Elapsed; // stopwatch elapsed
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + TimeSWE; } }
        public uint Timestamp32S => MiscProcedures.DateTimeToUint32seconds(DateTimeNowUtc);
        bool _disposing;
        Thread _engineThread;
        Thread _receiverThread;
        UdpClient _socket;
        internal ActionsQueue EngineThreadQueue;
        readonly Random _insecureRandom;
        internal Random InsecureRandom => _insecureRandom;
        Dictionary<RegistrationId, LocalDrpPeer> LocalPeers = new Dictionary<RegistrationId, LocalDrpPeer>(); // accessed only by engine thread       
        internal ConnectionToNeighbor[] ConnectedPeersByToken16 = new ConnectionToNeighbor[ushort.MaxValue+1];
      
        ushort _seq16Counter_AtoEP; // accessed only by engine thread
        internal NeighborPeerAckSequenceNumber16 GetNewNpaSeq16_AtoEP() => new NeighborPeerAckSequenceNumber16 { Seq16 = _seq16Counter_AtoEP++ };
        public DrpPeerEngineConfiguration Configuration { get; private set; }

        #region unique data filters
        /// <summary>
        /// is used to make sure that processed ECDH keys are unique
        /// </summary>
        internal readonly UniqueDataFilter RecentUniquePublicEcdhKeys = new UniqueDataFilter(10000);
        readonly UniqueDataFilter _recentUniqueRegistrationRequests = new UniqueDataFilter(1000);
        internal readonly UniqueDataFilter RecentUniqueInviteRequests = new UniqueDataFilter(1000);
        #endregion

        public DrpPeerEngine(DrpPeerEngineConfiguration configuration)
        {
            _insecureRandom = configuration.InsecureRandomSeed.HasValue ? new Random(configuration.InsecureRandomSeed.Value) : new Random();
            Configuration = configuration;
            Initialize(configuration);
            _seq16Counter_AtoEP = (ushort)_insecureRandom.Next(ushort.MaxValue);
            EngineThreadQueue = new ActionsQueue(exc => HandleExceptionInEngineThread(exc));

            _socket = new UdpClient(configuration.LocalPort ?? 0);
            _receiverThread = new Thread(ReceiverThreadEntry);
            _receiverThread.Name = "DRP receiver";
            _receiverThread.Priority = ThreadPriority.Highest;
            _receiverThread.Start();

            _engineThread = new Thread(EngineThreadEntry);
            _engineThread.Name = "DRP engine";
            _engineThread.Start();
        }
        partial void Initialize(DrpPeerEngineConfiguration configuration);
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;
            EngineThreadQueue.Dispose();
            _engineThread.Join();
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
            WriteToLog_receiver_detail($"received packet {packetType} from {remoteEndpoint} ({udpPayloadData.Length} bytes)");
            if (packetType == DrpPacketType.RegisterPow1Request)
            {
                ProcessRegisterPow1RequestPacket(remoteEndpoint, udpPayloadData);
                return;
            }

            if (packetType == DrpPacketType.RegisterReq)
            {
                if (RegisterRequestPacket.IsAtoEP(udpPayloadData))
                {
                    ProcessRegisterReqAtoEpPacket(remoteEndpoint, udpPayloadData);
                    return;
                }
            }

            var receivedAtUtc = DateTimeNowUtc;
            EngineThreadQueue.Enqueue(() =>
            {
                if (RespondersToRetransmittedRequests_ProcessPacket(remoteEndpoint, udpPayloadData)) return;
                if (PendingUdpRequests_ProcessPacket(remoteEndpoint, udpPayloadData, receivedAtUtc)) return;

                switch (packetType)
                {
                    case DrpPacketType.Ping:
                        {
                            var localRxToken16 = PingPacket.DecodeToken16FromUdpPayloadData(udpPayloadData);
                            var connectedPeer = ConnectedPeersByToken16[localRxToken16];
                            if (connectedPeer != null)
                                connectedPeer.OnReceivedPing(remoteEndpoint, udpPayloadData);
                        } break;
                    case DrpPacketType.Pong:
                        {
                            var localRxToken16 = PongPacket.DecodeToken16FromUdpPayloadData(udpPayloadData);
                            var connectedPeer = ConnectedPeersByToken16[localRxToken16];
                            if (connectedPeer != null)
                                connectedPeer.OnReceivedPong(remoteEndpoint, udpPayloadData, receivedAtUtc);
                        }
                        break;
                    case DrpPacketType.RegisterReq:
                        {
                            var localRxToken16 = RegisterRequestPacket.DecodeToken16FromUdpPayloadData_P2Pmode(udpPayloadData);
                            var connectedPeer = ConnectedPeersByToken16[localRxToken16];
                            if (connectedPeer != null)
                                connectedPeer.OnReceivedRegisterReq(remoteEndpoint, udpPayloadData);
                        }
                        break;
                    case DrpPacketType.InviteReq:
                        {
                            var localRxToken16 = RegisterRequestPacket.DecodeToken16FromUdpPayloadData_P2Pmode(udpPayloadData);
                            var connectedPeer = ConnectedPeersByToken16[localRxToken16];
                            if (connectedPeer != null)
                                connectedPeer.OnReceivedInviteSyn(remoteEndpoint, udpPayloadData);
                        }
                        break;
                }
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

        internal bool ValidateReceivedReqTimestamp32S(uint receivedSynTimestamp32S)
        {
            var differenceS = Math.Abs((int)receivedSynTimestamp32S - Timestamp32S);
            return differenceS < Configuration.MaxSynTimestampDifference;
        }
    }

    public interface IDrpRegisteredPeerApp 
    {
        void OnReceivedMessage(byte[] message);
        /// <summary>
        /// searches for a known user in local contact book
        /// </summary>
        DMP.UserId OnReceivedInvite_LookupUser(RegistrationId remoteRegID);

        SessionDescription OnReceivedInvite_GetLocalSessionDescription(DMP.UserId requesterUserId);
        void OnAcceptedIncomingInvite(Session session);
    }
}
