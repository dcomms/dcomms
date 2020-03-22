using Dcomms.DRP.Packets;
using Dcomms.P2PTP.Extensibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.P2PTP.LocalLogic
{
    /// <summary>
    /// stores connected peers, manages the connected peers and streams
    /// has a "manager" thread that runs all signaling and P2PTP logic
    /// </summary>
    class Manager : IDisposable
    {
        /// <summary>
        /// is executed by manager thread
        /// </summary>
        readonly ActionsQueue _actionsQueue;
        internal void InvokeInManagerThread(Action a, string actionVisibleId)
        {
            _actionsQueue.Enqueue(a, actionVisibleId);
        }

        /// <summary>
        /// connections with known remote testNodeId
        /// when remote server is restarted, connection goes to "pending peers"
        /// by remote peer id
        /// written+read by manager thread; read by GUI thread, locked when manager thread modifies or when GUI thread reads
        /// </summary>
        readonly Dictionary<PeerId, ConnectedPeer> _connectedPeers = new Dictionary<PeerId, ConnectedPeer>(); 
        /// <summary>
        /// temporary list to store the server peers when remote nodeId is unknown
        /// has peers with 1 stream only
        /// accessed by manager thread only
        /// </summary>
        readonly Dictionary<IPEndPoint, ConnectedPeer> _pendingPeers = new Dictionary<IPEndPoint, ConnectedPeer>(); 
        internal IConnectedPeer[] ConnectedPeers { get { lock (_connectedPeers) return _connectedPeers.Values.ToArray(); } }
        Thread _thread;
        readonly LocalPeer _localPeer;
        bool _disposing;

        public Manager(LocalPeer localPeer)
        {
            _localPeer = localPeer;
            localPeer.Manager = this;
            _actionsQueue = new ActionsQueue(exc => _localPeer.HandleException(LogModules.GeneralManager, exc), new ExecutionTimeStatsCollector(() => localPeer.DateTimeNow));

            if (_localPeer.Configuration.Coordinators != null)
            {      
                // connect to coordinator peers
                foreach (var serverEP in _localPeer.Configuration.Coordinators)
                    AddToPendingPeers(ConnectedPeerType.toConfiguredServer, serverEP, GetSocketForNewOutgoingStream());
             }

            _thread = new Thread(ThreadEntry);
            _thread.Name = "manager thread";
            _thread.Start();
        }
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;
            _actionsQueue.Dispose();

            _thread.Join();
        }

        internal bool IsItUniqueStreamId(StreamId streamId) // manager thread
        {
            foreach (var cp in _connectedPeers.Values)
                if (cp.Streams.ContainsKey(streamId))
                    return false;
            return true;
        }
        internal StreamId CreateNewUniqueStreamId()
        {
        _retry:
            var id = _localPeer.Random.Next();
            if (id == 0) id = 1;

            var r = new StreamId((uint)id);
            if (IsItUniqueStreamId(r) == false)
                goto _retry;

            return r;
        }

        int _indexForGetSocketForNewOutgoingStream = 0;
        SocketWithReceiver GetSocketForNewOutgoingStream()
        {
            return _localPeer.Receivers[(_indexForGetSocketForNewOutgoingStream++) % _localPeer.Receivers.Count];
        }
        void AddToPendingPeers(ConnectedPeerType type, IPEndPoint remoteEndpoint, SocketWithReceiver socket)
        {
            var cp = new ConnectedPeer(_localPeer, null, type, remoteEndpoint.Address);
            cp.TryAddStream(socket, remoteEndpoint, null, _pendingPeers.Values.Select(x => x.Streams.Values.Single().StreamId));
            // all "pending" streams will have unique local stream ID

            _pendingPeers.Add(remoteEndpoint, cp);
        }
                     
        void ThreadEntry()
        {
            int threadLoopCounter = 0;
            while (!_disposing)
            {
                try
                { // every 100ms (approximately)
                    if (threadLoopCounter % 10 == 0)
                    { // every 1 sec
                        SendHelloRequestsToPeers();
                        ShareConnectedPeers();
                    }
                    if (threadLoopCounter % 100 == 0)
                    { // every 10 sec
                        CleanObsoleteConnectedPeers();
                    }
                    if (threadLoopCounter % 1000 == 0)
                    { // every 100 sec
                        if (_localPeer.Configuration.RoleAsUser)
                            _ = ConfigureNAT();
                    }

                    foreach (var ext in _localPeer.Configuration.Extensions)
                        ext.OnTimer100msApprox();

                    _actionsQueue.ExecuteQueued();
                }
                catch (Exception exc)
                {
                    _localPeer.HandleException(LogModules.GeneralManager, exc);
                }
                Thread.Sleep(100);
                threadLoopCounter++;
            }
        }
             
        #region hello (connection, active/idle, handshaking, authorization) level
        DateTime? _nextTimeSendHelloRequestToPeers = null;

        void SendHelloRequestsToPeers() // manager thread
        {
            var now = _localPeer.DateTimeNowUtc;
            if (_nextTimeSendHelloRequestToPeers == null || now > _nextTimeSendHelloRequestToPeers.Value)
            {
                _nextTimeSendHelloRequestToPeers = now.Add(LocalLogicConfiguration.SendHelloRequestPeriod);               
                try
                {
                    foreach (var peer in _pendingPeers.Values.Union(_connectedPeers.Values))
                    {
                        bool itIsFirstStream = true;
                        foreach (var stream in peer.Streams.Values)
                        {
                            SendHelloRequestToPeer(now, peer, stream, itIsFirstStream && (peer.RemoteIpLocationData == null));
                            itIsFirstStream = false;
                        }
                    }
                }
                catch (Exception exc)
                {
                    _localPeer.HandleException(LogModules.Hello, exc);
                }
            }
        }
        void SendHelloRequestToPeer(DateTime now, ConnectedPeer peer, ConnectedPeerStream stream, bool requestIpLocation)
        {
            var data = new PeerHelloPacket(_localPeer, peer, stream, stream.LastTimeReceivedAccepted.HasValue ? PeerHelloRequestStatus.ping : PeerHelloRequestStatus.setup, requestIpLocation).Encode();
            stream.LastTimeSentRequest = now;
            stream.Socket.UdpSocket.Send(data, data.Length, stream.RemoteEndPoint); // send from all local sockets to all remote sockets to open P2P connection in the NAT
        }

        /// <summary>
        /// receiver thread
        /// changes state of connected peer
        /// </summary>
        internal void ProcessReceivedHello(byte[] udpData, IPEndPoint remoteEndpoint, SocketWithReceiver socket, uint packetReceivedTimestamp32)
        {
            // enqueue into manager thread // reduce load of the receiver thread
            _actionsQueue.Enqueue(() =>
            {
                var helloPacket = new PeerHelloPacket(udpData);
                
                if (_localPeer.IpLocationScraper == null && !String.IsNullOrEmpty(helloPacket.RequestedFromIp))
                {
                    _localPeer.IpLocationScraper = new IpLocationScraper(helloPacket.RequestedFromIp, _localPeer);
                }

                //if (_localPeer.NatTester == null)
                //{
                //    xxxx
                //}

                if (helloPacket.ToPeerId == null)
                {
                    if (helloPacket.Status.IsSetupOrPing())
                    {
                        if (_localPeer.Configuration.RoleAsCoordinator)
                            ProcessReceivedHello_SetupRequestFromNewPeer(helloPacket, remoteEndpoint, socket, _localPeer.LocalPeerId, packetReceivedTimestamp32);
                        else
                        {
                            _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                            PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_dontTryLater, null, socket, remoteEndpoint);
                        }
                    }
                    else
                        _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                }
                else if (helloPacket.ToPeerId.Equals(_localPeer.LocalPeerId) == false)
                { // bad ToPeerId  // can happen if this peer restarts
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                    if (helloPacket.Status.IsSetupOrPing())
                    {
                        PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_tryCleanSetup, null, socket, remoteEndpoint);
                    }
                }
                else if (_pendingPeers.ContainsKey(remoteEndpoint))
                {// correct ToPeerId and remote endpoint is in _initiallyNotRespondedServers
                    ProcessReceivedHello_FromPendingPeer(helloPacket, remoteEndpoint, packetReceivedTimestamp32);
                }
                else
                {// correct ToPeerId
                    if (_connectedPeers.TryGetValue(helloPacket.FromPeerId, out var connectedPeer))
                    { // packet from already connected peer
                        ProcessReceivedHello_FromExistingPeer(helloPacket, remoteEndpoint, socket, connectedPeer, packetReceivedTimestamp32);
                    }
                    else if (helloPacket.Status.IsSetupOrPing())
                    { // FromPeerId is unknown, ToPeerId is this one // request to set up connection from new peer
                        ProcessReceivedHello_SetupRequestFromNewPeer(helloPacket, remoteEndpoint, socket, null, packetReceivedTimestamp32);
                    }
                }
            }, "ProcessReceivedHello634569");
        }

        bool RemoteVersionIsAcceptableForNewConnection(PeerHelloPacket remoteHello)
        {
            return MiscProcedures.Uint32secondsToDateTime(remoteHello.LibraryVersion) > MiscProcedures.MinPeerCompilationDateTimeUtcExclusive;
        }

        /// <summary>
        /// for both coordinatorServer and userPeer
        /// accepts connection, adds peer and/or stream to lists, responds with 'accepted'
        /// </summary>
        void ProcessReceivedHello_SetupRequestFromNewPeer(PeerHelloPacket helloPacket, IPEndPoint remoteEndpoint, SocketWithReceiver socket, PeerId localPeerIdForResponse, uint requestReceivedTimestamp32) 
        {           
            if (_localPeer.Configuration.RoleAsCoordinator)
            {
                if (_connectedPeers.Count > LocalLogicConfiguration.CoordinatorPeer_MaxConnectedPeersToAccept)
                {
                    _localPeer.SysAdminFeedbackChannel.OnReachedMaxConnectedPeersAtThisCoordinatorServer();
                    PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_tryLater, null, socket, remoteEndpoint);
                    return;
                }
            }
            else if (_localPeer.Configuration.RoleAsSharedPassive)
            {
                if (_connectedPeers.Count > LocalLogicConfiguration.SharedPeer_MaxConnectedPeersToAccept)
                {
                    _localPeer.SysAdminFeedbackChannel.OnReachedMaxConnectedPeersAtThisSharedPeer();
                    _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"rejecting request from {remoteEndpoint}: _connectedPeers.Count={_connectedPeers.Count} > LocalLogicConfiguration.SharedPeer_MaxConnectedPeersToAccept");
                    PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_dontTryLater, null, socket, remoteEndpoint);
                    return;
                }
            }
            else if (_localPeer.Configuration.RoleAsUser)
            {
                if (_connectedPeers.Count > LocalLogicConfiguration.UserPeer_MaxConnectedPeersToAccept)
                {
                    _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"rejecting request from {remoteEndpoint}: _connectedPeers.Count={_connectedPeers.Count} > LocalLogicConfiguration.UserPeer_MaxConnectedPeersToAccept");
                    PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_dontTryLater, null, socket, remoteEndpoint);
                    return;
                }
                if (!RemoteVersionIsAcceptableForNewConnection(helloPacket))
                {
                    _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"rejecting request from {remoteEndpoint}: old version");
                    PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_dontTryLater, null, socket, remoteEndpoint);
                    return;
                }
            }
            
            if (!_connectedPeers.TryGetValue(helloPacket.FromPeerId, out var peer))
            {
                peer = new ConnectedPeer(_localPeer, helloPacket.FromPeerId, ConnectedPeerType.fromPeerAccepted, remoteEndpoint.Address);
                _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"accepted initial hello from peer {peer}");
                lock (_connectedPeers)
                    _connectedPeers.Add(helloPacket.FromPeerId, peer);
            }

            if (peer.Streams.Count > LocalLogicConfiguration.ConnectedPeerMaxStreamsCount)
            {
                _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"rejecting request from {remoteEndpoint}: peer.Streams.Count={peer.Streams.Count} > LocalLogicConfiguration.ConnectedPeerMaxStreamsCount");
                PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_dontTryLater, null, socket, remoteEndpoint);
            }
            else
            {
                var stream = peer.TryAddStream(socket, remoteEndpoint, helloPacket.StreamId);
              //  _localPeer.WriteToLog(LogModules.Hello, $"created new stream {stream} from new peer {peer}");

                var responseCpuDelayMs = (ushort)Math.Round(TimeSpan.FromTicks(unchecked(_localPeer.Time32 - requestReceivedTimestamp32)).TotalMilliseconds);
                PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.accepted, localPeerIdForResponse, socket, remoteEndpoint,
                    responseCpuDelayMs,
                    _localPeer.Configuration.RoleAsUser);
            }
        }
        void ProcessReceivedHello_FromExistingPeer(PeerHelloPacket helloPacket, IPEndPoint remoteEndpoint, SocketWithReceiver socket, ConnectedPeer connectedPeer, uint packetReceivedTimestamp32)
        {
            // current situation:
            // received hello packet; this peer wants to accept connection
            // in hello packet FromPeerId matches to existing connected peer
            // there is already at least 1 stream in the existing peer; it could be request via this stream or via another stream 
            // it could be request (ping) or response
            // it could be packet to new stream, or to existing stream
            // it could be packet from changed remote endpoint to same stream ID
            // it could be packet from wrong remote endpoint to new (non-existing locally) stream ID   ???????????????????????
            
            if (!connectedPeer.Streams.TryGetValue(helloPacket.StreamId, out var stream))
            { // received packet in new streamId
                if (helloPacket.Status.IsSetupOrPing())
                {
                    if (connectedPeer.Streams.Count > LocalLogicConfiguration.ConnectedPeerMaxStreamsCount)
                    {
                        _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"rejecting request from {remoteEndpoint}: connectedPeer.Streams.Count={connectedPeer.Streams.Count} > LocalLogicConfiguration.ConnectedPeerMaxStreamsCount");
                        PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_dontTryLater, null, socket, remoteEndpoint);
                        return;
                    }
                    stream = connectedPeer.TryAddStream(socket, remoteEndpoint, helloPacket.StreamId);
                  //   _localPeer.WriteToLog(LogModules.Hello, $"peer {connectedPeer} received hello from new stream {stream}");
                    if (stream == null) throw new InvalidOperationException();
                }
                else
                {
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                    return;
                }
            }
            else
            {// received packet in existing streamId
                // check source endpoint 
                if (!remoteEndpoint.Equals(stream.RemoteEndPoint))
                { // dont allow change of source ip/port within same stream
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                    if (helloPacket.Status.IsSetupOrPing())
                    {
                        PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.rejected_tryCleanSetup, null, socket, remoteEndpoint);
                    }
                    return;
                }
            }

            // current situation: we got connectedPeer, stream.  we handle request or response
            if (!RemoteVersionIsAcceptableForNewConnection(helloPacket))
            {
                _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"peer {connectedPeer} got response with too old version");
                connectedPeer.RemoveStream(stream);
                _connectedPeers.Remove(connectedPeer.RemotePeerId);
                return;
            }

            switch (helloPacket.Status)
            {
                case PeerHelloRequestStatus.setup:
                case PeerHelloRequestStatus.ping:
                    var responseCpuDelayMs = (ushort)Math.Round(TimeSpan.FromTicks(unchecked(_localPeer.Time32 - packetReceivedTimestamp32)).TotalMilliseconds);
                    PeerHelloPacket.Respond(helloPacket, PeerHelloRequestStatus.accepted, null, socket, remoteEndpoint, responseCpuDelayMs, _localPeer.Configuration.RoleAsUser,
                        _localPeer.IpLocationScraper?.IpLocationData//, _localPeer.NatTester?.NatTestResult
                        );
                    break;
                case PeerHelloRequestStatus.accepted:
                    ProcessReceivedAcceptedHello_UpdateHelloLevelFields(connectedPeer, stream, helloPacket, packetReceivedTimestamp32);
                    break;
                case PeerHelloRequestStatus.rejected_tryCleanSetup:
                    _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"peer {connectedPeer} received {helloPacket.Status} from stream {stream}");
                    connectedPeer.RemoveStream(stream);
                    AddToPendingPeers(connectedPeer.Type, remoteEndpoint, socket);
                    break;
                case PeerHelloRequestStatus.rejected_dontTryLater:
                    _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"peer {connectedPeer} received {helloPacket.Status} from stream {stream}");
                    connectedPeer.RemoveStream(stream);
                    break;
                case PeerHelloRequestStatus.rejected_tryLater:
                    _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"peer {connectedPeer} received {helloPacket.Status} from stream {stream}");
                    // it will try anyway on timer 
                    break;
            }
        }        
        void ProcessReceivedHello_FromPendingPeer(PeerHelloPacket helloPacket, IPEndPoint remoteEndpoint, uint packetReceivedTime32)
        {
            var pendingPeer = _pendingPeers[remoteEndpoint];
            var stream = pendingPeer.Streams.Values.Single(); // must be only 1 stream in the ConnectedPeer (as initially configured)
            if (!stream.StreamId.Equals(helloPacket.StreamId))
            {
                _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                return;
            }
            
            switch (helloPacket.Status)
            {
                case PeerHelloRequestStatus.accepted:
                    {
                        _pendingPeers.Remove(remoteEndpoint);

                        if (!_connectedPeers.TryGetValue(helloPacket.FromPeerId, out var alreadyRespondedPeer))
                        { // first-stream response from this server
                            pendingPeer.RemotePeerId = helloPacket.FromPeerId;
                            _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"received initial hello response from pending peer {pendingPeer}");
                            lock (_connectedPeers)
                                _connectedPeers.Add(helloPacket.FromPeerId, pendingPeer);
                            ProcessReceivedAcceptedHello_UpdateHelloLevelFields(pendingPeer, stream, helloPacket, packetReceivedTime32);
                        }
                        else
                        { // secondary streams response: move ConnectedPeerStream
                            _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"received secondary-stream hello response from {remoteEndpoint}, pending peer {alreadyRespondedPeer}");
                            if (!alreadyRespondedPeer.Streams.ContainsKey(stream.StreamId))
                            {
                                pendingPeer.RemoveStream(stream);
                                alreadyRespondedPeer.TryAddStream(stream.Socket, stream.RemoteEndPoint, stream.StreamId);
                                
                                //  alreadyRespondedPeer.Streams.Add(stream.StreamId, stream);
                                //  foreach (var sx in stream.Extensions)
                                //       sx.Value.OnMovedToConnectedPeer();
                                ProcessReceivedAcceptedHello_UpdateHelloLevelFields(alreadyRespondedPeer, stream, helloPacket, packetReceivedTime32);
                            }
                            else throw new Exception("alreadyRespondedPeer.Streams.ContainsKey(stream.StreamId)");
                        }
                    }
                    break;
                case PeerHelloRequestStatus.rejected_dontTryLater:
                    {
                        _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"received {helloPacket.Status} hello response from pending peer {pendingPeer}");
                        _pendingPeers.Remove(remoteEndpoint);
                    }
                    break;
                case PeerHelloRequestStatus.rejected_tryCleanSetup:
                    {
                        _localPeer.WriteToLog_deepDetail(LogModules.Hello, $"received {helloPacket.Status} hello response from pending peer {pendingPeer}");
                        stream.StreamId = _localPeer.Manager.CreateNewUniqueStreamId();
                        _pendingPeers.Remove(remoteEndpoint);
                    }
                    break;
            }
        }
        void ProcessReceivedAcceptedHello_UpdateHelloLevelFields(ConnectedPeer connectedPeer, ConnectedPeerStream stream, PeerHelloPacket helloResponsePacket, uint responseReceivedTime32)
        {
            var rtt = TimeSpan.FromTicks(unchecked(responseReceivedTime32 - helloResponsePacket.RequestTime32)) - TimeSpan.FromMilliseconds(helloResponsePacket.ResponseCpuDelayMs ?? 0);
            if (rtt < TimeSpan.Zero) rtt = TimeSpan.Zero; // avoid abnormal measurements
            stream.LatestHelloRtt = rtt;
            stream.LocalPeerPublicIp = helloResponsePacket.RequestedFromIp;
            stream.LastTimeReceivedAccepted = _localPeer.DateTimeNowUtc; 
            connectedPeer.ProtocolVersion = helloResponsePacket.ProtocolVersion;
            connectedPeer.LibraryVersion = MiscProcedures.Uint32secondsToDateTime(helloResponsePacket.LibraryVersion);
            connectedPeer.TotalHelloAcceptedPacketsReceived++;
            stream.TotalHelloAcceptedPacketsReceived++;
            stream.RemotePeerRoleIsUser = helloResponsePacket.RoleFlagIsUser;

            if (helloResponsePacket.IpLocationData != null && connectedPeer.RemoteIpLocationData == null)
            {
                _localPeer.WriteToLog_higherLevelDetail(LogModules.Hello, $"got IP location from peer {connectedPeer}: {helloResponsePacket.IpLocationData}");
                connectedPeer.RemoteIpLocationData = helloResponsePacket.IpLocationData;
            }

            if (helloResponsePacket.ExtensionIds != null && helloResponsePacket.ExtensionIds.Length != 0)
                connectedPeer.LatestReceivedRemoteExtensionIds = new HashSet<string>(helloResponsePacket.ExtensionIds.Distinct());
        }
        #endregion

        void CleanObsoleteConnectedPeers() // manager thread
        {
            var now = _localPeer.DateTimeNowUtc;
            
            var idlePeers = new LinkedList<ConnectedPeer>();
            foreach (var peer in _connectedPeers.Values)
            {
                if (peer.Type != ConnectedPeerType.toConfiguredServer)
                {
                    var idleStreams = new LinkedList<ConnectedPeerStream>();
                    foreach (var stream in peer.Streams.Values)
                        if (stream.IsIdle(now, LocalLogicConfiguration.MaxPeerIdleTime_ToRemove))
                            idleStreams.AddLast(stream);
                    foreach (var idleStream in idleStreams)
                    {
                        peer.RemoveStream(idleStream); // this removes the stream from SUBT sender thread
                    }
                }

                if (peer.Streams.Count == 0)
                    idlePeers.AddLast(peer);
            }

            if (idlePeers.Count != 0)
                lock (_connectedPeers)
                    foreach (var idlePeer in idlePeers)
                    {
                       // _localPeer.WriteToLog(LogModules.Hello, $"removing idle peer {idlePeer}");
                        _connectedPeers.Remove(idlePeer.RemotePeerId);
                    }

            // reinitialize when not receiving any response from remote streams  (try re-create sockets when network adapter has  changed)
            if (_localPeer.Configuration.RoleAsCoordinator == false)
            {
                var lastTimeReceivedAccepted = (_connectedPeers.Values.Union(_pendingPeers.Values)).Select(
                    peer => peer.Streams.Values.Select(stream => stream.LastTimeActiveNotIdle
                        ).DefaultIfEmpty(DateTime.MinValue).Max()
                    ).DefaultIfEmpty(DateTime.MinValue).Max();
                if (now - lastTimeReceivedAccepted > LocalLogicConfiguration.ReinitializationTimeout)
                {
                    Reinitialize();
                }
            }
        }
        internal void Reinitialize() // manager thread
        {
            _disposing = true;
            _actionsQueue.Dispose();
            _localPeer.Reinitialize_CalledByManagerOnly();
        }

        #region p2p connections sharing
        DateTime? _nextTimeShareConnectedPeers = null;
        /// <summary>
        /// runs on coordinator peer
        /// shares all peers with type = fromPeerAccepted
        /// </summary>
        void ShareConnectedPeers()
        {
            if (_nextTimeShareConnectedPeers == null || _localPeer.DateTimeNowUtc > _nextTimeShareConnectedPeers.Value)
            {
                _nextTimeShareConnectedPeers = _localPeer.DateTimeNowUtc.Add(LocalLogicConfiguration.SharePeerConnectionsPeriod);              
               // var now = _localPeer.DateTimeNowUtc;

                /*
                 general algorithm:                 
                find out list of extensionSets   
                group by extensionsSets
                make groups - all streams of conn. peers
                send the groups to every peer in group                                (to 1 stream)                 
                dont go over limit per packet                
                 */

                var extensions = new HashSet<string>();
                foreach (var cp in _connectedPeers.Values)
                    if (cp.LatestReceivedRemoteExtensionIds != null)
                        foreach (var extension in cp.LatestReceivedRemoteExtensionIds)
                            if (!extensions.Contains(extension)) extensions.Add(extension);
                foreach (var x in extensions) ShareConnectedPeers(x);
                ShareConnectedPeers((string)null);
            }           
        }
        void ShareConnectedPeers(string extension)
        {
            if (extension == null)
                ShareConnectedPeers(_connectedPeers.Values.Where(cp => cp.LatestReceivedRemoteExtensionIds == null || cp.Type == ConnectedPeerType.toConfiguredServer).ToArray());
            else
                ShareConnectedPeers(_connectedPeers.Values.Where(cp => (cp.LatestReceivedRemoteExtensionIds != null && cp.LatestReceivedRemoteExtensionIds.Contains(extension)) || cp.Type == ConnectedPeerType.toConfiguredServer).ToArray());
        }      
        void ShareConnectedPeers(ConnectedPeer[] connectedPeers)
        {
            var now = _localPeer.DateTimeNowUtc;
            var l = connectedPeers.Length;
            if (l > 1)
            {
                int numberOfRandomPairs = l / 2;
                for (int i = 0; i < numberOfRandomPairs; i++)
                {
                    var index1 = _localPeer.Random.Next(l);
                _loop:
                    var index2 = _localPeer.Random.Next(l);
                    if (index2 == index1) goto _loop;

                    ShareConnectedPeers(connectedPeers[index1], connectedPeers[index2], now);
                }
            }

        }

        static IEnumerable<ConnectedPeerStream> SelectWithUniqueRemoteEndPoints(IEnumerable<ConnectedPeerStream> streams)
        {
            var seenKeys = new HashSet<IPEndPoint>();
            foreach (var s in streams)
                if (seenKeys.Add(s.RemoteEndPoint))
                    yield return s;              
        }
        void ShareConnectedPeers(ConnectedPeer connectedPeer1, ConnectedPeer connectedPeer2, DateTime now)
        {
            try
            {
                var streams1 = SelectWithUniqueRemoteEndPoints(connectedPeer1.Streams.Values.Where(x => x.IsIdle(now, LocalLogicConfiguration.MaxPeerIdleTime_ToShare) == false)).ToArray();        
                var streams2 = SelectWithUniqueRemoteEndPoints(connectedPeer2.Streams.Values.Where(x => x.IsIdle(now, LocalLogicConfiguration.MaxPeerIdleTime_ToShare) == false)).ToArray();
                var l = Math.Min(streams1.Length, streams2.Length);
                if (l == 0) return;
                var stream1 = streams1[0];
                var stream2 = streams2[0];
                if (stream1.RemoteEndPoint.Address.Equals(stream2.RemoteEndPoint.Address)) return; // dont connect peers across lan (from same IP)

                var itemsForPacket1 = new PeersListPacket_SharedPeerIpv4[l];
                var itemsForPacket2 = new PeersListPacket_SharedPeerIpv4[l];
                for (int i = 0; i < l; i++)
                {
                    itemsForPacket1[i] = new PeersListPacket_SharedPeerIpv4(streams1[i].StreamId, connectedPeer2.RemotePeerId, streams2[i].RemoteEndPoint);
                    itemsForPacket2[i] = new PeersListPacket_SharedPeerIpv4(streams2[i].StreamId, connectedPeer1.RemotePeerId, streams1[i].RemoteEndPoint);
                }
                
                var data1 = new PeersListPacketIpv4(itemsForPacket1, _localPeer.LocalPeerId, stream1.StreamId).Encode();
                stream1.Socket.UdpSocket.Send(data1, data1.Length, stream1.RemoteEndPoint);

                var data2 = new PeersListPacketIpv4(itemsForPacket2, _localPeer.LocalPeerId, stream2.StreamId).Encode();
                stream2.Socket.UdpSocket.Send(data2, data2.Length, stream2.RemoteEndPoint);
            }
            catch (Exception exc)
            {
                _localPeer.HandleException(LogModules.PeerSharing, exc);
            }
        }
     
        internal void ProcessReceivedSharedPeers(byte[] data, IPEndPoint remoteEndpoint)
        {
            // enqueue into manager thread // reduce load of the receiver thread
            _actionsQueue.Enqueue(() =>
            {
                var peersListPacket = new PeersListPacketIpv4(data);
                if (_connectedPeers.TryGetValue(peersListPacket.FromPeerId, out var connectedPeer))
                { //authorized here
                    ProcessReceivedPeersList(connectedPeer, peersListPacket, remoteEndpoint);
                }
                else
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);               
            }, "ProcessReceivedSharedPeers5670"); 
        }
        void ProcessReceivedPeersList(ConnectedPeer connectedPeer, PeersListPacketIpv4 peersListPacket, IPEndPoint remoteEndpoint)
        {
            if (!connectedPeer.Streams.TryGetValue(peersListPacket.StreamId, out var localStream))
            {
                _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                return;
            }
            
            foreach (var receivedSharedPeer in peersListPacket.SharedPeers)
            {
                if (receivedSharedPeer.ToPeerId.Equals(_localPeer.LocalPeerId)) continue; // dont connect to this peer

                if (!_connectedPeers.TryGetValue(receivedSharedPeer.ToPeerId, out var localConnectedPeer))
                {
                    _localPeer.WriteToLog_deepDetail(LogModules.PeerSharing, $"received shared peer {receivedSharedPeer}) from {connectedPeer}");
                    localConnectedPeer = new ConnectedPeer(_localPeer, receivedSharedPeer.ToPeerId, ConnectedPeerType.toPeerShared, receivedSharedPeer.ToEndPoint.Address);
                    lock (_connectedPeers)
                        _connectedPeers.Add(receivedSharedPeer.ToPeerId, localConnectedPeer);
                }
                if (!localConnectedPeer.Streams.Values.Any(x => x.RemoteEndPoint.Equals(receivedSharedPeer.ToEndPoint)))
                {
                    if (connectedPeer.Streams.TryGetValue(receivedSharedPeer.FromSocketAtStreamId, out var fromStreamSocket))
                        localConnectedPeer.TryAddStream(
                            fromStreamSocket.Socket,
                            receivedSharedPeer.ToEndPoint, null);
                } // else there is already connected stream to this remote peer endpoint
            }
        }
        #endregion

        internal void ProcessReceivedExtensionSignalingPacket(BinaryReader reader, IPEndPoint remoteEndpoint)
        {
            // enqueue into manager thread // reduce load of the receiver thread
            _actionsQueue.Enqueue(() =>
            {
                (var fromPeerId, var toPeerId, var streamId, var extensionId) = ExtensionProcedures.ParseExtensionSignalingPacket(reader);

                if (_localPeer.LocalPeerId.Equals(toPeerId) == false)
                {
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                    return;
                }

                if (fromPeerId == null || !_connectedPeers.TryGetValue(fromPeerId, out var connectedPeer))
                {
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                    return;
                }

                if (!connectedPeer.Streams.TryGetValue(streamId, out var stream))
                {
                    _localPeer.Firewall.OnUnauthenticatedReceivedPacket(remoteEndpoint);
                    return;
                }

                if (_localPeer.ExtensionsById.TryGetValue(extensionId, out var extension))
                {
                    if (stream.Extensions.TryGetValue(extension, out var streamExtension))
                        streamExtension.OnReceivedSignalingPacket(reader);
                }
            }, "ProcessReceivedExtensionSignalingPacket3458");
        }
        
        async Task ConfigureNAT()
        {
            try
            {
                var endpoints = _localPeer.Receivers.Select(x => x.UdpSocket.Client?.LocalEndPoint);
                var localUdpPorts = endpoints.Select(x => x as IPEndPoint).Where(x => x != null).Select(x => x.Port).ToArray();
                using var nu = new NAT.NatUtility(_localPeer.VisionChannel, _localPeer.VisionChannelSourceId);
                await nu.SearchAndConfigure(localUdpPorts);
            }
            catch (Exception exc)
            {
                _localPeer.HandleException(LogModules.Nat, exc);
            }      
        }

        internal void ProcessReceivedNat1TestRequest(SocketWithReceiver socket, byte[] udpData, IPEndPoint remoteEndpoint) // receiver thread
        {
            // todo put some limits here per endpoint? or we don't care?
            var request = NatTest1RequestPacket.Decode(udpData);
            var response = new NatTest1ResponsePacket { RequesterEndpoint = remoteEndpoint, Token32 = request.Token32 };
            var responseData = response.Encode();
            socket.UdpSocket.Send(responseData, responseData.Length, remoteEndpoint);
        }
    }
}
