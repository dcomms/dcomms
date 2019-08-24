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
    /// operates one UDP socket
    /// </summary>
    public partial class DrpPeerEngine : IDisposable
    {
        readonly ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        internal ICryptoLibrary CryptoLibrary => _cryptoLibrary;
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
        readonly Random _insecureRandom = new Random();
        internal Random InsecureRandom => _insecureRandom;
        Dictionary<RegistrationPublicKey, LocalDrpPeer> LocalPeers = new Dictionary<RegistrationPublicKey, LocalDrpPeer>(); // accessed only by manager thread
       
        internal ConnectionToNeighbor[] ConnectedPeersByToken16 = new ConnectionToNeighbor[ushort.MaxValue];
        DrpPeerEngine _engine;
        ushort _seq16Counter;
        internal NextHopAckSequenceNumber16 GetNewNhaSeq16() => new NextHopAckSequenceNumber16 { Seq16 = _seq16Counter++ };
        public DrpPeerEngineConfiguration Configuration { get; private set; }

        public DrpPeerEngine(DrpPeerEngineConfiguration configuration)
        {
            Configuration = configuration;
            Initialize(configuration);
            _seq16Counter = (ushort)_insecureRandom.Next(ushort.MaxValue);
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
        partial void Initialize(DrpPeerEngineConfiguration configuration);
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

        #region error handlers / dev vision / anti-fraud
        const string VisionChannelObjectName_reg_requesterSide = "reg.requester";
        const string VisionChannelObjectName_reg_responderSide = "reg.responder";
        const string VisionChannelObjectName_reg_rpSide = "reg.rp";
        const string VisionChannelObjectName_engineThread = "engineThread";
        const string VisionChannelObjectName_receiverThread = "receiverThread";

        void WriteToLog_receiver_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_receiverThread, null) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_receiverThread, null, AttentionLevel.detail, message);

        }
        void HandleExceptionInReceiverThread(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_receiverThread, null) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_receiverThread, null, AttentionLevel.strongPain, $"exception: {exc}");
        }
        void HandleExceptionInEngineThread(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_engineThread, null) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_engineThread, null, AttentionLevel.strongPain, $"exception: {exc}");
        }
        internal void WriteToLog_reg_requesterSide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_reg_requesterSide, null) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_reg_requesterSide, null, AttentionLevel.detail, message);
        }
        void WriteToLog_reg_requesterSide_mediumPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_reg_requesterSide, null) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_reg_requesterSide, null, AttentionLevel.detail, message);

        }
        void HandleExceptionWhileConnectingToRP(IPEndPoint rpEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_reg_requesterSide, null) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_reg_requesterSide, null, AttentionLevel.detail, $"exception while connecting to RP {rpEndpoint}: {exc}");

            // todo: analyse if it is malformed packet received from attacker's RP
        }
        internal void HandleGeneralException(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(null, null) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(null, null, AttentionLevel.strongPain, $"general exception: {message}");
        }
        internal void OnReceivedUnauthorizedSourceIpPacket(IPEndPoint remoteEndpoint)
        {
        }
        internal void OnReceivedBadRegisterSynPow1(IPEndPoint remoteEndpoint)
        {
        }
        internal void OnReceivedRegisterSynAtoRpPacketFromUnknownSource(IPEndPoint remoteEndpoint)
        { }
        internal void OnReceivedRegisterSynAtoRpPacketWithBadPow2(IPEndPoint remoteEndpoint)
        { }
        void OnReceivedBadSignature(IPEndPoint remoteEndpoint)
        {
        }
        void HandleExceptionWhileConnectingToA(IPEndPoint remoteEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_reg_responderSide, null) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_reg_responderSide, null, AttentionLevel.detail, $"exception while connecting to A {remoteEndpoint}: {exc}");
        }
        void WriteToLog_reg_responderSide_detail(string sourceCodePlaceId, string message = null)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(VisionChannelObjectName_reg_responderSide, sourceCodePlaceId) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(VisionChannelObjectName_reg_responderSide, sourceCodePlaceId, AttentionLevel.detail, message);
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
            WriteToLog_receiver_detail($"received packet {packetType} from {remoteEndpoint}");
            if (packetType == DrpPacketType.RegisterPow1RequestPacket)
            {
                ProcessRegisterPow1RequestPacket(remoteEndpoint, udpPayloadData);
                return;
            }

            var receivedAtUtc = DateTimeNowUtc;
            if (packetType == DrpPacketType.RegisterSynPacket)
            {
                if (RegisterSynPacket.IsAtoRP(udpPayloadData))
                {
                    ProcessRegisterSynAtoRpPacket(remoteEndpoint, udpPayloadData, receivedAtUtc);
                    return;
                }
            }

            _engineThreadQueue.Enqueue(() =>
            {
                // process responses to  low-level UDP requests
                if (PendingUdpRequests_ProcessPacket(remoteEndpoint, udpPayloadData, receivedAtUtc))
                    return;

                switch (packetType)
                {
                    case DrpPacketType.PingRequestPacket:
                        {
                            var localRxToken16 = PingRequestPacket.DecodeToken16FromUdpPayloadData(udpPayloadData);
                            var connectedPeer = ConnectedPeersByToken16[localRxToken16];
                            if (connectedPeer != null)
                                connectedPeer.OnReceivedPingRequestPacket(remoteEndpoint, udpPayloadData);
                        } break;
                    case DrpPacketType.PingResponsePacket:
                        {
                            var localRxToken16 = PingResponsePacket.DecodeToken16FromUdpPayloadData(udpPayloadData);
                            var connectedPeer = ConnectedPeersByToken16[localRxToken16];
                            if (connectedPeer != null)
                                connectedPeer.OnReceivedPingResponsePacket(remoteEndpoint, udpPayloadData, receivedAtUtc);
                        } break;
                }


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
            foreach (var localPeer in LocalPeers.Values)
            {
              _restart_loop:
                foreach (var connectedPeer in localPeer.ConnectedPeers)
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
        bool TrySendRequestViaConnectedPeer(ConnectionToNeighbor connectedPeer, RegistrationPublicKey remotePeerRegistrationPublicKey)
        {
            // assert tx rate is not exceeded  -- return false

            // create an instance of TxRequestState, add it to list

            // send packet to peer

            throw new NotImplementedException();
        }
    }



    public class DrpPeerEngineConfiguration
    {
        public ushort? LocalPort;
        public IPAddress LocalForcedPublicIpForRegistration;
        public TimeSpan PingRequestsInterval = TimeSpan.FromSeconds(2);
        public double PingRetransmissionInterval_RttRatio = 2.0; // "how much time to wait until sending another ping request?" - coefficient, relative to previously measured RTT
        public TimeSpan ConnectedPeersRemovalTimeout => PingRequestsInterval + TimeSpan.FromSeconds(2);
        
        public uint RegisterPow1_RecentUniqueDataResetPeriodS = 10 * 60;
        public int RegisterPow1_MaxTimeDifferenceS = 20 * 60;
        public bool RespondToRegisterPow1Errors = false;

        public TimeSpan Pow2RequestStatesTablePeriod = TimeSpan.FromSeconds(5);
        public int Pow2RequestStatesTableMaxSize = 100000;
        public int Timestamp32S_MaxDifferenceToAccept = 20 * 60;

        public TimeSpan PendingRegisterRequestsTimeout = TimeSpan.FromSeconds(20);

        public double UdpLowLevelRequests_ExpirationTimeoutS = 2;
        public double UdpLowLevelRequests_InitialRetransmissionTimeoutS = 0.2;
        public double UdpLowLevelRequests_RetransmissionTimeoutIncrement = 1.5;
        public double RegSynAckRequesterSideTimoutS = 10;

        public double InitialPingRequests_ExpirationTimeoutS = 5;
        public double InitialPingRequests_InitialRetransmissionTimeoutS = 0.1;
        public double InitialPingRequests_RetransmissionTimeoutIncrement = 1.05;

        public VisionChannel VisionChannel;
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

}
