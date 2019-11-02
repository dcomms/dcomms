using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    /// <summary>
    /// "contact point" of local user in the regID space
    /// can be "registered" or "registering"
    /// </summary>
    public partial class LocalDrpPeer : IDisposable, IVisibleModule, IVisiblePeer
    {
        /// <summary>
        /// is used for:
        /// - PoW1
        /// - EpEndpoint vlidateion at responder
        /// - RequesterEndpoint validation at requester
        /// </summary>
        public IPAddress PublicIpApiProviderResponse;

        readonly LocalDrpPeerConfiguration _configuration;
        public LocalDrpPeerConfiguration Configuration => _configuration;

        double[] _cachedLocalPeerRegistrationIdVectorValues;
        public double[] LocalPeerRegistrationIdVectorValues
        {
            get
            {
                if (_cachedLocalPeerRegistrationIdVectorValues == null)
                {
                    _cachedLocalPeerRegistrationIdVectorValues = RegistrationIdDistance.GetVectorValues(CryptoLibrary, Configuration.LocalPeerRegistrationId, Engine.NumberOfDimensions);
                }
                return _cachedLocalPeerRegistrationIdVectorValues;
            }
        }

        readonly IDrpRegisteredPeerApp _drpPeerApp;
        internal readonly DrpPeerEngine Engine;
        internal ICryptoLibrary CryptoLibrary => Engine.CryptoLibrary;

        string IVisibleModule.Status => $"connected neighbors: {ConnectedNeighbors.Count}/{_configuration.MinDesiredNumberOfNeighbors}. {CurrentRegistrationOperationsCount} pending reg.";

        public LocalDrpPeer(DrpPeerEngine engine, LocalDrpPeerConfiguration configuration, IDrpRegisteredPeerApp drpPeerApp)
        {
            Engine = engine;
            _configuration = configuration;
            _drpPeerApp = drpPeerApp;
            engine.Configuration.VisionChannel?.RegisterVisibleModule(engine.Configuration.VisionChannelSourceId, this.ToString(), this);
        }
        public List<ConnectionToNeighbor> ConnectedNeighbors = new List<ConnectionToNeighbor>();
        public ushort ConnectedNeighborsBusySectorIds
        {
            get
            {
                ushort r = 0;
                foreach (var n in ConnectedNeighbors.Where(x => x.CanBeUsedForNewRequests))
                    r |= n.SectorIndexFlagsMask;
                return r;
            }
        }

        #region IVisiblePeer implementation
        float[] IVisiblePeer.VectorValues => RegistrationIdDistance.GetVectorValues(CryptoLibrary, _configuration.LocalPeerRegistrationId, Engine.NumberOfDimensions).Select(x => (float)x).ToArray();
        bool IVisiblePeer.Highlighted => false;
        string IVisiblePeer.Name => Engine.Configuration.VisionChannelSourceId;
        IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => ConnectedNeighbors;
        string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer)
        {
            throw new NotImplementedException();
        }
        #endregion

        public IEnumerable<ConnectionToNeighbor> GetConnectedNeighborsForRouting(RoutedRequest routedRequest)
        {
            foreach (var connectedPeer in ConnectedNeighbors.Where(x => x.CanBeUsedForNewRequests))
            {
                if (routedRequest.ReceivedFromNeighborNullable != null && connectedPeer == routedRequest.ReceivedFromNeighborNullable)
                {
                    Engine.WriteToLog_routing_detail($"skipping routing back to source peer {connectedPeer.RemoteRegistrationId}", routedRequest.Req, this);
                    continue;
                }
                if (routedRequest.TriedNeighbors.Contains(connectedPeer))
                {
                    Engine.WriteToLog_routing_detail($"skipping routing to previously tried peer {connectedPeer.RemoteRegistrationId}", routedRequest.Req, this);
                    continue;
                }

                if (routedRequest.RequesterRegistrationId.Equals(connectedPeer.RemoteRegistrationId))
                {
                    Engine.WriteToLog_routing_detail($"skipping routing to peer with same regID {connectedPeer.RemoteRegistrationId}", routedRequest.Req, this);
                    continue;
                }

                yield return connectedPeer;
            }
        }

        //public IEnumerable<ConnectionToNeighbor> GetConnectedNeighborsForRouting(ConnectionToNeighbor sourceNeighborNullable,
        //    HashSet<ConnectionToNeighbor> alreadyTriedProxyingToDestinationPeersNullable,
        //    InviteRequestPacket req)
        //{
        //    foreach (var connectedPeer in ConnectedNeighbors.Where(x => x.CanBeUsedForNewRequests))
        //    {
        //        if (sourceNeighborNullable != null && connectedPeer == sourceNeighborNullable)
        //        {
        //            Engine.WriteToLog_routing_detail($"skipping routing back to source peer {connectedPeer.RemoteRegistrationId}", req, this);
        //            continue;
        //        }
        //        if (alreadyTriedProxyingToDestinationPeersNullable != null && alreadyTriedProxyingToDestinationPeersNullable.Contains(connectedPeer))
        //        {
        //            Engine.WriteToLog_routing_detail($"skipping routing to previously tried peer {connectedPeer.RemoteRegistrationId}", req, this);
        //            continue;
        //        }

        //        if (req.RequesterRegistrationId.Equals(connectedPeer.RemoteRegistrationId))
        //        {
        //            Engine.WriteToLog_routing_detail($"skipping routing to peer with same regID {connectedPeer.RemoteRegistrationId}", req, this);
        //            continue;
        //        }

        //        yield return connectedPeer;
        //    }
        //}

        public void AddToConnectedNeighbors(ConnectionToNeighbor newConnectedNeighbor, RegisterRequestPacket req)
        {
            newConnectedNeighbor.OnP2pInitialized();
            ConnectedNeighbors.Add(newConnectedNeighbor);

            Engine.WriteToLog_p2p_higherLevelDetail(newConnectedNeighbor, $"added new connection to list of neighbors: {newConnectedNeighbor.RemoteRegistrationId} to {newConnectedNeighbor.RemoteEndpoint}", req);
        }
        public int CurrentRegistrationOperationsCount;

        public void Dispose() // unregisters
        {

        }
        public override string ToString() => $"localDrpPeer{_configuration.LocalPeerRegistrationId}";

        #region on timer 
        DateTime? _latestConnectToNewNeighborOperationStartTimeUtc;
        async Task ConnectToNewNeighborIfNeededAsync(DateTime timeNowUtc)
        {
            if (ConnectedNeighbors.Count < _configuration.MinDesiredNumberOfNeighbors)
            {
                if ((CurrentRegistrationOperationsCount == 0) ||
                    timeNowUtc > _latestConnectToNewNeighborOperationStartTimeUtc + TimeSpan.FromSeconds(Engine.Configuration.NeighborhoodExtensionMaxRetryIntervalS))
                {
                    if (_latestConnectToNewNeighborOperationStartTimeUtc.HasValue)
                    {
                        if ((timeNowUtc - _latestConnectToNewNeighborOperationStartTimeUtc.Value).TotalSeconds < Engine.Configuration.NeighborhoodExtensionMinIntervalS)
                            return;
                    }

                    _latestConnectToNewNeighborOperationStartTimeUtc = timeNowUtc;

                    try
                    {
                        //    extend neighbors via ep (3% probability)  or via existing neighbors --- increase mindistance, from 1
                        var connectedNeighborsForRequest = ConnectedNeighbors.Where(x => x.CanBeUsedForNewRequests).ToList();
                        if (this.Configuration.EntryPeerEndpoints != null && (Engine.InsecureRandom.NextDouble() < 0.01 || connectedNeighborsForRequest.Count == 0))
                        {
                            var epEndpoint = this.Configuration.EntryPeerEndpoints[Engine.InsecureRandom.Next(this.Configuration.EntryPeerEndpoints.Length)];
                            Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via EP {epEndpoint} ({connectedNeighborsForRequest.Count} connected operable neighbors now)", null, null);
                            await Engine.RegisterAsync(this, epEndpoint, 0, RegisterRequestPacket.MaxNumberOfHopsRemaining);
                        }
                        else
                        {
                            if (connectedNeighborsForRequest.Count != 0)
                            {
                                var neighborToSendRegister = connectedNeighborsForRequest[Engine.InsecureRandom.Next(connectedNeighborsForRequest.Count)];
                                Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via neighbor {neighborToSendRegister} ({connectedNeighborsForRequest.Count} connected operable neighbors now)", null, null);
                                await neighborToSendRegister.RegisterAsync(0, ConnectedNeighborsBusySectorIds, RegisterRequestPacket.MaxNumberOfHopsRemaining,
                                    (byte)Engine.InsecureRandom.Next(10)
                                    );
                            }
                        }
                    }
                    catch (RequestRejectedException exc)
                    {
                        Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"failed to extend neighbors for {this}: {exc}", null, null);
                    }
                    catch (Exception exc)
                    {
                        Engine.WriteToLog_reg_requesterSide_mediumPain($"failed to extend neighbors for {this}: {exc}", null, null);
                    }
                }
            }
        }

        DateTime? _lastTimeDetroyedWorstNeighborUtc;
        /// <summary>
        /// when needed (too many neighbors / too old p2p connections / etc)
        /// destroys worst P2P connection: sends PING with connection teardown flag set, and destroys the connection
        /// </summary>
        void NeighborsApoptosisProcedure(DateTime timeNowUtc)
        {
            if (ConnectedNeighbors.Count > Configuration.AbsoluteMaxNumberOfNeighbors)
            {
                DestroyWorstNeighbor(null, timeNowUtc);
            }
            else if (ConnectedNeighbors.Count > Configuration.SoftMaxNumberOfNeighbors)
            {
                DestroyWorstNeighbor(P2pConnectionValueCalculator.MutualValueToKeepConnectionAlive_SoftLimitNeighborsCountCases, timeNowUtc);
            }
            else if (ConnectedNeighbors.Count >= Configuration.MinDesiredNumberOfNeighbors)
            {
                if (Configuration.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS.HasValue)
                {
                    if (_lastTimeDetroyedWorstNeighborUtc == null || (timeNowUtc - _lastTimeDetroyedWorstNeighborUtc.Value).TotalSeconds > Configuration.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS)
                        DestroyWorstNeighbor(null, timeNowUtc);
                }
            }
        }
        void DestroyWorstNeighbor(double? mutualValueLowLimit, DateTime timeNowUtc)
        {
            if (ConnectedNeighbors.Any(x => x.IsInTeardownState))
            {
                return;
            }

            double? worstValue = mutualValueLowLimit;
            ConnectionToNeighbor worstNeighbor = null;
            foreach (var neighbor in ConnectedNeighbors.Where(x => x.CanBeUsedForNewRequests))
            {
                var p2pConnectionValue_withNeighbor =
                    P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary,
                        this.Configuration.LocalPeerRegistrationId, this.ConnectedNeighborsBusySectorIds,
                        neighbor.RemoteRegistrationId, neighbor.RemoteNeighborsBusySectorIds ?? 0, Engine.NumberOfDimensions);
                Engine.WriteToLog_p2p_higherLevelDetail(neighbor, $"@DestroyWorstNeighbor() p2pConnectionValue_withNeighbor={p2pConnectionValue_withNeighbor} from {this} to {neighbor}", null);
                if (worstValue == null || p2pConnectionValue_withNeighbor < worstValue)
                {
                    worstValue = p2pConnectionValue_withNeighbor;
                    worstNeighbor = neighbor;
                }
            }

            if (worstNeighbor != null)
            {
                _lastTimeDetroyedWorstNeighborUtc = timeNowUtc;

                Engine.WriteToLog_p2p_higherLevelDetail(worstNeighbor, $"destroying worst P2P connection with neighbor. neighbors count = {ConnectedNeighbors.Count}", null);
                var ping = worstNeighbor.CreatePing(false, true, 0);

                var pendingPingRequest = new PendingLowLevelUdpRequest(worstNeighbor.RemoteEndpoint,
                                PongPacket.GetScanner(worstNeighbor.LocalNeighborToken32, ping.PingRequestId32), Engine.DateTimeNowUtc,
                                Engine.Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                                ping.Encode(),
                                Engine.Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS,
                                Engine.Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                            );

                _ = Engine.SendUdpRequestAsync_Retransmit(pendingPingRequest); // retransmit until PONG    
                worstNeighbor.IsInTeardownState = true;
                Engine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(PingPacket.ConnectionTeardownStateDurationS), () =>
                {
                    if (!worstNeighbor.IsDisposed)
                    {
                        Engine.WriteToLog_p2p_higherLevelDetail(worstNeighbor, $"destroying worst P2P connection after teardown state timeout", null);
                        worstNeighbor.Dispose();
                    }
                });
            }
        }

        internal void EngineThreadOnTimer100ms(DateTime timeNowUtc)
        {
            _ = ConnectToNewNeighborIfNeededAsync(timeNowUtc);
            NeighborsApoptosisProcedure(timeNowUtc);
        }
        #endregion

        async Task BeginConnectToEPsAsync(IPEndPoint[] endpoints)// engine thread
        {
            foreach (var endpoint in endpoints)
            {
                try
                {
                    var conn = await Engine.RegisterAsync(this, endpoint, 0, 1); // engine thread
                    Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"@BeginConnectToEPsAsync connected to {endpoint}. {ConnectedNeighbors.Count} connected neighbors", null, null);
                }
                catch (Exception exc)
                {
                    Engine.HandleGeneralException($"connecting to another EP {endpoint} failed", exc);

                }
            }
        }
        public void BeginConnectToEPs(IPEndPoint[] endpoints, Action cb)
        {
            Engine.EngineThreadQueue.Enqueue(async () =>
            {
                try
                {
                    await BeginConnectToEPsAsync(endpoints);
                    cb();
                }
                catch (Exception exc)
                {
                    Engine.HandleGeneralException("BeginConnectToEPs failed", exc);
                }
            });

        }
    }

    /// <summary>
    /// configuration of locally hosted DRP peer
    /// optionally contains config for registration
    /// </summary>
    public class LocalDrpPeerConfiguration
    {
        public IPEndPoint[] EntryPeerEndpoints; // in case when local peer IP = entry peer IP, it is skipped
        public RegistrationId LocalPeerRegistrationId { get; private set; }
        public RegistrationPrivateKey LocalPeerRegistrationPrivateKey { get; private set; }
        public int? MinDesiredNumberOfNeighbors;// = 12;
        public int? SoftMaxNumberOfNeighbors;// = 13; 
        public int? AbsoluteMaxNumberOfNeighbors;// = 20;
        public double? MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS;// = 30;

        public static LocalDrpPeerConfiguration CreateWithNewKeypair(ICryptoLibrary cryptoLibrary)
        {
            var privatekey = new RegistrationPrivateKey { ed25519privateKey = cryptoLibrary.GeneratePrivateKeyEd25519() };
            return new LocalDrpPeerConfiguration
            {
                LocalPeerRegistrationPrivateKey = privatekey,
                LocalPeerRegistrationId = new RegistrationId(cryptoLibrary.GetPublicKeyEd25519(privatekey.ed25519privateKey))
            };
        }
    }
}
