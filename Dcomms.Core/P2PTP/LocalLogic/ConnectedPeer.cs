using Dcomms.P2PTP.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dcomms.P2PTP.LocalLogic
{
    /// <summary>
    /// connection between local peer and remote peer 
    /// </summary>
    internal class ConnectedPeer: IConnectedPeer
    {
        readonly LocalPeer _localPeer;
        internal readonly Dictionary<ILocalPeerExtension, IConnectedPeerExtension> Extensions;
        internal HashSet<string> LatestReceivedRemoteExtensionIds;
        internal IpLocationData RemoteIpLocationData;
        internal readonly string RemoteIp;

        /// <param name="remotePeerId">null for 'pending' peers</param>
        internal ConnectedPeer(LocalPeer localPeer, PeerId remotePeerId, ConnectedPeerType type, IPAddress remoteIp)
        {
            RemoteIp = remoteIp.ToString();
            Type = type;
            RemotePeerId = remotePeerId;
            _localPeer = localPeer;
            Extensions = localPeer.Configuration.Extensions.ToDictionary(ext => ext, ext => ext.OnConnectedPeer(this));
        }

        /// <summary>
        /// by localStreamId 
        /// written by manager thread only
        /// read by manager and UI threads
        /// is locked when read by GUI thread and when modified by manager thread
        /// </summary>
        internal readonly Dictionary<StreamId, ConnectedPeerStream> Streams = new Dictionary<StreamId, ConnectedPeerStream>();

        internal bool IsIdle(DateTime now, TimeSpan maxIdleTime)
        {
            if (Streams.Count == 0) return true;
            var latestActiveTime = Streams.Values.Max(x => x.LastTimeActiveNotIdle);
            return now - latestActiveTime > maxIdleTime;
        }

        /// <summary>
        /// the only procedure which creates/adds streams
        /// manager thread
        /// this will initiate hello-level packets to remote peer
        /// </summary>
        /// <param name="streamId">
        /// if null, it generates random ID, using optional notAllowedStreamIds
        /// </param>
        /// <returns>
        /// null is stream with this StreamId already exists
        /// </returns>
        internal ConnectedPeerStream TryAddStream(SocketWithReceiver socket, IPEndPoint remoteEP, StreamId streamId = null, IEnumerable<StreamId> notAllowedStreamIds = null)
        {
            if (streamId == null)
            {
            _retry:
                streamId = _localPeer.Manager.CreateNewUniqueStreamId();
                if (Streams.ContainsKey(streamId)) goto _retry;
                if (notAllowedStreamIds != null)
                    if (notAllowedStreamIds.Any(x => x.Equals(streamId))) goto _retry;
            }
            else
            {
                if (_localPeer.Manager.IsItUniqueStreamId(streamId) == false) return null;
            }
            
            var r = new ConnectedPeerStream(_localPeer, this, streamId, remoteEP, socket);
            lock (Streams)
                Streams.Add(streamId, r);
            socket.OnCreatedDestroyedStream(r, true);
            return r;
        }
        /// <summary>
        /// the only procedure which removes/destroys streams
        /// </summary>
        internal void RemoveStream(ConnectedPeerStream stream)
        {
            lock (Streams)
                Streams.Remove(stream.StreamId);
            stream.Socket.OnCreatedDestroyedStream(stream, false);
            foreach (var streamExtension in stream.Extensions.Values)
                streamExtension.OnDestroyed();
        }

        internal PeerId RemotePeerId;

        // internal ConnectedPeerState State;
        internal readonly ConnectedPeerType Type;
        internal DateTime? LibraryVersion;
        internal ushort? ProtocolVersion;
        DateTime? IConnectedPeer.RemoteLibraryVersion => LibraryVersion;

        internal int TotalHelloAcceptedPacketsReceived;

        IConnectedPeerStream[] IConnectedPeer.Streams { get { lock (Streams) return Streams.Values.ToArray(); } }
        IDictionary<ILocalPeerExtension, IConnectedPeerExtension> IConnectedPeer.Extensions => Extensions;
        PeerId IConnectedPeer.RemotePeerId => RemotePeerId;
        ConnectedPeerType IConnectedPeer.Type => Type;
        IpLocationData IConnectedPeer.RemoteIpLocationData => RemoteIpLocationData;
        string IConnectedPeer.RemoteIp => RemoteIp;
        
        public string ToString(LocalPeer localPeer)
        {
            return RemoteIp;

            //var sb = new StringBuilder();
            //sb.AppendFormat("{0}_{1}(", Type, RemotePeerId);
            //foreach (var stream in Streams.Values)
            //{
            //    sb.Append(stream.ToString());
            //    if (localPeer != null)
            //        sb.AppendFormat(" hr/c: {0:0.0}sec ago", (localPeer.DateTimeNowUtc - (stream.LastTimeReceivedAccepted ?? stream.Created)).TotalSeconds);
            //    sb.Append(";");
            //}

            //sb.Append(")");
            //if (LibraryVersion != null)
            //    sb.AppendFormat("lib:{0:yyMMdd-HH:mm}", LibraryVersion);
            //if (ProtocolVersion != null)
            //    sb.AppendFormat("p:{0}", ProtocolVersion);
            
            //return sb.ToString();
        }
        public override string ToString()
        {
            return ToString(null);
        }       
    }
    //internal enum ConnectedPeerState
    //{
    //    configuredServerNotResponded,
    //    receivedHello, // initial communication
    //    sentHello, // initial communication
    //    veryfyingLicense,
    //    verifiedLicense,
    //    operational, // after successful authorization
    //    idle, // when not received hello for long time - removed from Dictionary
        
    //}
}
