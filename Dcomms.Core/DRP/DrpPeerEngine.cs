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
        uint Timestamp32S => MiscProcedures.DateTimeToUint32(DateTimeNowUtc);
        bool _disposing;
        Thread _engineThread;
        Thread _receiverThread;
        UdpClient _socket;
        ActionsQueue _engineThreadQueue;
        readonly Random _insecureRandom = new Random();
        internal Random InsecureRandom => _insecureRandom;
        Dictionary<RegistrationPublicKey, LocalDrpPeer> LocalPeers = new Dictionary<RegistrationPublicKey, LocalDrpPeer>(); // accessed only by engine thread       
        internal ConnectionToNeighbor[] ConnectedPeersByToken16 = new ConnectionToNeighbor[ushort.MaxValue+1];
      
        ushort _seq16Counter; // accessed only by engine thread
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

            if (packetType == DrpPacketType.RegisterSyn)
            {
                if (RegisterSynPacket.IsAtoRP(udpPayloadData))
                {
                    ProcessRegisterSynAtoEpPacket(remoteEndpoint, udpPayloadData);
                    return;
                }
            }

            var receivedAtUtc = DateTimeNowUtc;
            _engineThreadQueue.Enqueue(() =>
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

            // remove expired responders
            RespondersToRetransmittedRequests_OnTimer100ms(timeNowUTC);
        }
        #endregion

        public void BeginSendInvite(RegistrationPublicKey localPeerRegistrationPublicKey, RegistrationPublicKey remotePeerRegistrationPublicKey, byte[] message, Action<DrpResponderStatusCode> callback)
        {
            // find RegisteredLocalDrpPeer

            // find closest neighbor to destination

            // send invite

            // subroutine create requestViaConnectedPeer
        }
    }

    public interface IDrpRegisteredPeerUser
    {
        void OnReceivedMessage(byte[] message);
    }
}
