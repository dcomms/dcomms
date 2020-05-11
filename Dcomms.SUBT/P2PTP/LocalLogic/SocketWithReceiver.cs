using Dcomms.DSP;
using Dcomms.P2PTP.Extensibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dcomms.P2PTP.LocalLogic
{
    /// <summary>
    /// owns UDP socket
    /// runs receiver thread
    /// </summary>
    public class SocketWithReceiver: IDisposable
    {
        internal readonly UdpClient UdpSocket;
        readonly Thread _thread;
        bool _disposing;
        readonly LocalPeer _localPeer;
        /// <summary>
        /// is executed by receiver thread
        /// </summary>
        readonly ActionsQueue _actionsQueue;

        /// <summary>
        /// accessed by this receiver thread only
        /// duplicate hashtable with streams, in addition to ConnectedPeer.Streams (which is managed by manager thread)
        /// </summary>
        readonly Dictionary<StreamId, ConnectedPeerStream> _streams = new Dictionary<StreamId, ConnectedPeerStream>();

        public SocketWithReceiver(LocalPeer localPeer, UdpClient udpSocket)
        {
            _localPeer = localPeer;
            _actionsQueue = new ActionsQueue(exc => _localPeer.HandleException(LogModules.GeneralManager, exc), new ExecutionTimeStatsCollector(() => localPeer.DateTimeNow));
            UdpSocket = udpSocket;

            _thread = new Thread(ThreadEntry);
            _thread.Name = "receiver " + LocalEndPointString;
            _thread.Priority = ThreadPriority.Highest;
            _thread.Start();
        }
        public string LocalEndPointString => UdpSocket.Client?.LocalEndPoint.ToString();
        public override string ToString() => LocalEndPointString;

        /// <summary>
        /// is executed by manager thread
        /// passes the stream to receiver thread and updates streams hash table of this receiver
        /// </summary>
        internal void OnCreatedDestroyedStream(ConnectedPeerStream stream, bool createdOrDestroyed)
        {
            _actionsQueue.Enqueue(() =>
            {
                if (createdOrDestroyed)
                {
                    if (!_streams.ContainsKey(stream.StreamId))
                    {
                        _streams.Add(stream.StreamId, stream); // todo can be exception of duplicate key in rare cases?


                        if (_localPeer.Configuration.RoleAsUser)
                        {
                            if (_streams.Count > 150) _localPeer.WriteToLog_lightPain(LogModules.Receiver, $"receiver streams leak, count = {_streams.Count}");
                        }
                        else
                        {
                            if (_streams.Count > 1500) _localPeer.WriteToLog_lightPain(LogModules.Receiver, $"receiver streams leak, count = {_streams.Count}");
                        }
                    }
                }
                else
                    _streams.Remove(stream.StreamId);
            }, "OnCreatedDestroyedStream5259");
        }

        IirFilterCounter _pps = new IirFilterCounter(TimeSpan.TicksPerMillisecond * 100, TimeSpan.TicksPerSecond);
        IirFilterCounter _bps = new IirFilterCounter(TimeSpan.TicksPerMillisecond * 100, TimeSpan.TicksPerSecond);
        public string PerformanceString => $"{_pps.OutputPerUnit.PpsToString()};{_bps.OutputPerUnit.BandwidthToString()}";
        uint? _previousTimestamp32;
        void ThreadEntry()
        {
            IPEndPoint remoteEndpoint = default(IPEndPoint);
            while (!_disposing)
            {
                try
                {
                    _actionsQueue.ExecuteQueued();

                    var udpData = UdpSocket.Receive(ref remoteEndpoint);

                    var timestamp32 = _localPeer.Time32;
                    if (_previousTimestamp32.HasValue)
                    {
                        var timePassed32 = unchecked(timestamp32 - _previousTimestamp32.Value);
                        _pps.Input(1, timePassed32);
                        _bps.Input((udpData.Length + LocalLogicConfiguration.IpAndUdpHeadersSizeBytes) * 8, timePassed32);
                    }
                    _previousTimestamp32 = timestamp32;

                    var manager = _localPeer.Manager;
                    if (manager != null && _localPeer.Firewall.PacketIsAllowed(remoteEndpoint) && udpData.Length > 4)
                    {
                        if (udpData[0] == (byte)PacketTypes.NatTest1Request)
                        {
                            manager.ProcessReceivedNat1TestRequest(this, udpData, remoteEndpoint);
                        }
                        else
                        {

                            var packetType = P2ptpCommon.DecodeHeader(udpData);
                            if (packetType.HasValue)
                            {
                                switch (packetType.Value)
                                {
                                    case PacketTypes.hello:
                                        manager.ProcessReceivedHello(udpData, remoteEndpoint, this, timestamp32);
                                        break;
                                    case PacketTypes.peersListIpv4:
                                        manager.ProcessReceivedSharedPeers(udpData, remoteEndpoint);
                                        break;
                                    case PacketTypes.extensionSignaling:
                                        manager.ProcessReceivedExtensionSignalingPacket(BinaryProcedures.CreateBinaryReader(udpData, P2ptpCommon.HeaderSize), remoteEndpoint);
                                        break;
                                }
                            }
                            else
                            {
                                (var extension, var streamId, var index) = ExtensionProcedures.ParseReceivedExtensionPayloadPacket(udpData, _localPeer.Configuration.Extensions);
                                if (extension != null)
                                {
                                    if (_streams.TryGetValue(streamId, out var stream))
                                    {
                                        stream.Extensions.TryGetValue(extension, out var streamExtension);
                                        streamExtension.OnReceivedPayloadPacket(udpData, index);
                                    }
                                    //else _localPeer.WriteToLog(LogModules.Receiver, $"receiver {SocketInfo} got packet from bad stream id {streamId}");
                                }
                            }
                        }
                    }
                }
                //   catch (InvalidOperationException)
                //   {// intentionally ignored   (before "connection")
                //   }
                catch (SocketException exc)
                {
                    if (_disposing) return;
                    if (exc.ErrorCode != 10054) // forcibly closed - ICMP port unreachable - it is normal when peer gets down
                        _localPeer.HandleException(LogModules.Receiver, exc);
                    // else ignore it
                }
                catch (Exception exc)
                {
                    _localPeer.HandleException(LogModules.Receiver, exc);
                }
            }
        }
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;

            UdpSocket.Close();
            UdpSocket.Dispose();

            _thread.Join();
        }
    }
}
