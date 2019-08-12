using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.DSP;
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
        Random _insecureRandom = new Random();
        Dictionary<RegistrationPublicKey, LocalDrpPeer> LocalPeers = new Dictionary<RegistrationPublicKey, LocalDrpPeer>(); // accessed only by manager thread
        P2pConnectionToken32 GenerateNewUniqueLocalRxToken32()
        {
            for (int i = 0; i < 100; i++)
            {
                var r = new P2pConnectionToken32 { Token32 = (uint)_insecureRandom.Next() };
                var rToken16 = r.Token16;
                if (LocalPeers.Values.Any(lp => lp.ConnectedPeers.Any(cp => cp.LocalRxToken32.Token16 == rToken16)) == false)
                    return r;
            }
            throw new Exception();
        }
        DrpPeerEngine _engine;
        ushort _seq16Counter;
        internal NextHopAckSequenceNumber16 GetNewNhaSeq16() => new NextHopAckSequenceNumber16 { Seq16 = _seq16Counter++ };

        public DrpPeerEngine(DrpPeerEngineConfiguration configuration)
        {
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
        void WriteToLog_reg_requesterSide_warning(string message)
        {

        }
        void HandleExceptionWhileConnectingToRP(IPEndPoint rpEndpoint, Exception exc)
        {
            HandleException(exc, $"exception while connecting to RP {rpEndpoint}");
            // todo: analyse if it is malformed packet received from attacker's RP
        }
        public void HandleGeneralException(string message)
        {

        }
        #endregion

        #region registration RP-side
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

            var receivedAtUtc = DateTimeNowUtc;
            _engineThreadQueue.Enqueue(() =>
            {
                // process responses to  low-level UDP requests
                if (PendingUdpRequests_ProcessPacket(remoteEndpoint, udpPayloadData, receivedAtUtc))
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
            foreach (var localPeer in LocalPeers.Values)
            {
                foreach (var connectedPeer in localPeer.ConnectedPeers)
                    connectedPeer.OnTimer100ms(timeNowUTC);
            }
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
