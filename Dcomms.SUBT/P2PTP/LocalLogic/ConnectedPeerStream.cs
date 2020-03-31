using Dcomms.P2PTP.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dcomms.P2PTP.LocalLogic
{
    internal class ConnectedPeerStream: IConnectedPeerStream
    {
        readonly LocalPeer _localPeer;
        internal ConnectedPeerStream(LocalPeer localPeer, ConnectedPeer connectedPeer, StreamId streamId,
            IPEndPoint remoteEP, SocketWithReceiver socket)
        {
            _localPeer = localPeer;
            Created = localPeer.DateTimeNowUtc;
            StreamId = streamId;
            RemoteEndPoint = remoteEP;
            Socket = socket;
            Extensions = connectedPeer.Extensions.ToDictionary(ext => ext.Key, ext => ext.Value.OnConnectedPeerStream(this));
        }

        public StreamId StreamId { get; internal set; }
        /// <summary>
        /// is used:
        /// 1) to send outgoing packets (to remote peer)
        /// 2) to validate source of incoming packets (for matched StreamId also validate source IP and port)
        /// </summary>
        internal readonly IPEndPoint RemoteEndPoint;
        internal readonly SocketWithReceiver Socket;

        internal readonly DateTime Created;
        internal DateTime? LastTimeSentRequest; // ping or setup
        internal DateTime? LastTimeReceivedAccepted;
        internal TimeSpan? LatestHelloRtt;
        internal string LocalPeerPublicIp { get; set; }
        DateTime? _lastTimeMarkedActiveByExtension;    
        internal DateTime LastTimeActiveNotIdle
        {
            get
            {
                var r = LastTimeReceivedAccepted ?? Created;
                if (_lastTimeMarkedActiveByExtension.HasValue && _lastTimeMarkedActiveByExtension.Value > r)
                    r = _lastTimeMarkedActiveByExtension.Value;
                return r;
            }
        }
        public override string ToString()
        {
            return $"{RemoteEndPoint}:{StreamId}";
        }
        /// <summary>
        /// considers "accepted" responses to "hello" only
        /// </summary>
        internal bool IsIdle(DateTime now, TimeSpan maxIdleTime)
        {
            return now - LastTimeActiveNotIdle > maxIdleTime;
        }

        void IConnectedPeerStream.SendPacket(byte[] data, int length)
        {            
            Socket.UdpSocket.Send(data, length, RemoteEndPoint);
        }
        void IConnectedPeerStream.MarkAsActiveByExtension() => _lastTimeMarkedActiveByExtension = _localPeer.DateTimeNowUtc;
        bool IConnectedPeerStream.IsIdle(DateTime now, TimeSpan maxIdleTime) => IsIdle(now, maxIdleTime);
        TimeSpan? IConnectedPeerStream.LatestHelloRtt => LatestHelloRtt;

        /// <summary>
        /// read by receiver thread, initialized by manager thread
        /// </summary>
        internal readonly Dictionary<ILocalPeerExtension, IConnectedPeerStreamExtension> Extensions;
        IDictionary<ILocalPeerExtension, IConnectedPeerStreamExtension> IConnectedPeerStream.Extensions => Extensions;
        StreamId IConnectedPeerStream.StreamId => StreamId;

        public string LocalRemoteEndPointString => $"{Socket.LocalEndPointString}-{RemoteEndPoint.ToString()}";
        public string RemoteEndPointString => $"{RemoteEndPoint?.ToString()}";

        internal int TotalHelloAcceptedPacketsReceived;
        public bool RemotePeerRoleIsUser { get; internal set; }
        public string P2ptpActivityString => String.Format("RTT: {0}; LocalPublicIP: {1}; hR/c: -{2}, {3}",
            MiscProcedures.TimeSpanToString(LatestHelloRtt),
            LocalPeerPublicIp,
            MiscProcedures.TimeSpanToString(_localPeer.DateTimeNowUtc - (LastTimeReceivedAccepted ?? Created)),
            TotalHelloAcceptedPacketsReceived);
        /// <summary>
        /// if set to true, stops the debugger in some specific place, specific in every case of debugging
        /// </summary>
        public bool Debug { get; set; }
    }
}
