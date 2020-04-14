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

        public string Status => $"connected neighbors: {ConnectedNeighbors.Count}/{_configuration.MinDesiredNumberOfNeighbors}. {CurrentRegistrationOperationsCount} pending reg.";
        public bool IsConnected => ConnectedNeighbors.Count > 2;

        public LocalDrpPeer(DrpPeerEngine engine, LocalDrpPeerConfiguration configuration, IDrpRegisteredPeerApp drpPeerApp)
        {
            Engine = engine;
            _configuration = configuration;
            _drpPeerApp = drpPeerApp;
            engine.Configuration.VisionChannel?.RegisterVisibleModule(engine.Configuration.VisionChannelSourceId, this.ToString(), this);
        }
        public List<ConnectionToNeighbor> ConnectedNeighbors = new List<ConnectionToNeighbor>();
        public IEnumerable<ConnectionToNeighbor> ConnectedNeighborsCanBeUsedForNewRequests
        {
            get
            {
                var now = Engine.DateTimeNowUtc;
                return ConnectedNeighbors.Where(x => x.CanBeUsedForNewRequests(now));
            }
        }
        public ushort ConnectedNeighborsBusySectorIds
        {
            get
            {
                ushort r = 0;
                foreach (var n in ConnectedNeighborsCanBeUsedForNewRequests)
                    r |= n.SectorIndexFlagsMask;
                return r;
            }
        }
        public bool AnotherNeighborToSameSectorExists(ConnectionToNeighbor neighbor)
        {
            ushort r = 0;
            foreach (var n in ConnectedNeighborsCanBeUsedForNewRequests)
                if (n != neighbor && n.SectorIndexFlagsMask == neighbor.SectorIndexFlagsMask)
                    return true;
            return false;
        }

        #region IVisiblePeer implementation
        float[] IVisiblePeer.VectorValues => RegistrationIdDistance.GetVectorValues(CryptoLibrary, _configuration.LocalPeerRegistrationId, Engine.NumberOfDimensions).Select(x => (float)x).ToArray();
        bool IVisiblePeer.Highlighted => false;
        string IVisiblePeer.Name => Engine.Configuration.VisionChannelSourceId;
        
        IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => ConnectedNeighborsCanBeUsedForNewRequests;
        string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer)
        {
            throw new NotImplementedException();
        }
        #endregion

        public IEnumerable<ConnectionToNeighbor> GetConnectedNeighborsForRouting(RoutedRequest routedRequest)
        {
            foreach (var connectedPeer in ConnectedNeighborsCanBeUsedForNewRequests)
            {
                if (routedRequest.ReceivedFromNeighborNullable != null && connectedPeer == routedRequest.ReceivedFromNeighborNullable)
                {
                    if (routedRequest.Logger.WriteToLog_deepDetail_enabled)
                        routedRequest.Logger.WriteToLog_deepDetail($"skipping routing back to source peer {connectedPeer}");
                    continue;
                }
                if (routedRequest.TriedNeighbors.Contains(connectedPeer))
                {
                    if (routedRequest.Logger.WriteToLog_deepDetail_enabled)
                        routedRequest.Logger.WriteToLog_deepDetail($"skipping routing to previously tried peer {connectedPeer}");
                    continue;
                }

                if (routedRequest.RequesterRegistrationId.Equals(connectedPeer.RemoteRegistrationId))
                {
                    if (routedRequest.RegisterReq == null || routedRequest.RegisterReq.AllowConnectionsToRequesterRegistrationId != true)
                    {
                        if (routedRequest.Logger.WriteToLog_deepDetail_enabled)
                            routedRequest.Logger.WriteToLog_deepDetail($"skipping routing to peer with same regID {connectedPeer}");
                        continue;
                    }
                }

                yield return connectedPeer;
            }
        }
        
        public void AddToConnectedNeighbors(ConnectionToNeighbor newConnectedNeighbor, RegisterRequestPacket req)
        {
            newConnectedNeighbor.OnP2pInitialized();
            ConnectedNeighbors.Add(newConnectedNeighbor);

            Engine.WriteToLog_p2p_higherLevelDetail(newConnectedNeighbor, $"added new connection to list of neighbors: {newConnectedNeighbor} to {newConnectedNeighbor.RemoteEndpoint}", req);
        }
        public int CurrentRegistrationOperationsCount;

        public void Dispose() // unregisters
        {
            foreach (var conn in ConnectedNeighbors.ToList())
                conn.Dispose();

        }
        public override string ToString() => $"localDrpPeer{_configuration.LocalPeerRegistrationId}";
 
        internal void TestDirection(Logger logger, RegistrationId destinationRegId)
        {
            if (this.Configuration.LocalPeerRegistrationId.Equals(destinationRegId) == true) return;
            if (this.Configuration.AbsoluteMaxNumberOfNeighbors == 1) return; // special case: mobile device connected to home device
            var diff = RegistrationId.GetDifferenceVector(this.Configuration.LocalPeerRegistrationId, destinationRegId, CryptoLibrary, Engine.Configuration.SandboxModeOnly_NumberOfDimensions);                
            _ = TestDirection(logger, diff);
        }
        async Task TestDirection(Logger logger, double[] vectorFromThisToDestination, int iteration = 0)
        {
            // are all vectors along directionVector?
            bool neighbor_along_destinationVector_exists = false;

            foreach (var neighbor in ConnectedNeighborsCanBeUsedForNewRequests)
            {
                var thisRegIdVector = RegistrationIdDistance.GetVectorValues(CryptoLibrary, this.Configuration.LocalPeerRegistrationId, vectorFromThisToDestination.Length);
                var neighborRegIdVector = RegistrationIdDistance.GetVectorValues(CryptoLibrary, neighbor.RemoteRegistrationId, vectorFromThisToDestination.Length);
                var vectorFromLocalPeerToNeighbor = new double[thisRegIdVector.Length];
                for (int i = 0; i < vectorFromLocalPeerToNeighbor.Length; i++)
                    vectorFromLocalPeerToNeighbor[i] = RegistrationIdDistance.GetDifferenceInLoopedRegistrationIdSpace(thisRegIdVector[i], neighborRegIdVector[i]);
                               
                double multProduct = 0;
                for (int dimensionI = 0; dimensionI < vectorFromThisToDestination.Length; dimensionI++)
                    multProduct += vectorFromLocalPeerToNeighbor[dimensionI] * vectorFromThisToDestination[dimensionI];
                if (multProduct > 0)
                {
                    neighbor_along_destinationVector_exists = true;
                    break;
                }
            }
            if (neighbor_along_destinationVector_exists == false)
            {
                if (iteration < 8)
                {
                    if (ConnectedNeighbors.Count < _configuration.AbsoluteMaxNumberOfNeighbors)
                    {                    
                        logger.WriteToLog_higherLevelDetail_EmitListOfPeers($"no neighbors to destination {MiscProcedures.VectorToString(vectorFromThisToDestination)}, sending REGISTER request... iteration={iteration}", this);
                        
                        // try to fix the pain: connect to neighbors at empty direction
                        await ConnectToNewNeighborAsync(Engine.DateTimeNowUtc, true, vectorFromThisToDestination);
                        if (iteration >= 2)
                            await Engine.EngineThreadQueue.WaitAsync(TimeSpan.FromSeconds(10), "fixing empty direction 1237");

                        await TestDirection(logger, vectorFromThisToDestination, iteration + 1);
                    }
                    else
                        logger.WriteToLog_lightPain_EmitListOfPeers($"no neighbors to destination {MiscProcedures.VectorToString(vectorFromThisToDestination)} after {iteration} iterations. {ConnectedNeighbors.Count} connected neighbors already", this);

                }
                else
                { // pain is not fixed 
                    logger.WriteToLog_lightPain_EmitListOfPeers($"no neighbors to destination {MiscProcedures.VectorToString(vectorFromThisToDestination)} after {iteration} iterations. {ConnectedNeighbors.Count} connected neighbors", this);
                }
            }
        }

        #region on timer 
        DateTime? _latestConnectToNewNeighborOperationStartTimeUtc;
        DateTime? _latestEmptyDirectionsTestTimeUtc;
        int _neighborhoodExtensionFailuresCountInArow = 0;
        async Task ConnectToNewNeighborAsync(DateTime timeNowUtc, bool connectAnyway, double[] directionVectorNullable)
        {
            var connectedNeighborsForNewRequests = ConnectedNeighborsCanBeUsedForNewRequests.ToList();
            if (connectAnyway || connectedNeighborsForNewRequests.Count < _configuration.MinDesiredNumberOfNeighbors)
            {
                if (connectAnyway || (CurrentRegistrationOperationsCount == 0) ||
                    timeNowUtc > _latestConnectToNewNeighborOperationStartTimeUtc + TimeSpan.FromSeconds(Engine.Configuration.NeighborhoodExtensionMaxRetryIntervalS))
                {
                    if (connectAnyway == false)
                    {
                        if (_latestConnectToNewNeighborOperationStartTimeUtc.HasValue && 
                            (timeNowUtc - _latestConnectToNewNeighborOperationStartTimeUtc.Value).TotalSeconds < Engine.Configuration.NeighborhoodExtensionMinIntervalS)
                            return; // avoid too frequent registrations
                        if (Engine.PowThreadQueueCount != 0) // avoid having concurrent PoW operations, it leads to 200sec+ PoW delays when mobile device has bad internet connection
                            return;
                        if (ConnectedNeighbors.Count >= Configuration.AbsoluteMaxNumberOfNeighbors)
                            return;

                        Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood: {connectedNeighborsForNewRequests.Count} neighbors now", null, null);
                    }
                 
                    _latestConnectToNewNeighborOperationStartTimeUtc = timeNowUtc;

                    try
                    {
                        // extend neighbors via ep (3% probability)  or via existing neighbors --- increase mindistance, from 1                       
                        if (this.Configuration.EntryPeerEndpoints != null && (Engine.InsecureRandom.NextDouble() < 0.01 || connectedNeighborsForNewRequests.Count == 0))
                        {
                            var epEndpoint = this.Configuration.EntryPeerEndpoints[Engine.InsecureRandom.Next(this.Configuration.EntryPeerEndpoints.Length)];
                            Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via EP {epEndpoint} ({connectedNeighborsForNewRequests.Count} connected operable neighbors now)", null, null);
                            await Engine.RegisterAsync(this, epEndpoint, 0, RegisterRequestPacket.MaxNumberOfHopsRemaining, directionVectorNullable, connectedNeighborsForNewRequests.Count == 0);
                        }
                        else
                        {
                            if (connectedNeighborsForNewRequests.Count != 0)
                            {
                                var neighborToSendRegister = connectedNeighborsForNewRequests[Engine.InsecureRandom.Next(connectedNeighborsForNewRequests.Count)];
                                Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via neighbor {neighborToSendRegister} ({connectedNeighborsForNewRequests.Count} connected operable neighbors now)", null, null);
                                await neighborToSendRegister.RegisterAsync(0, ConnectedNeighborsBusySectorIds, RegisterRequestPacket.MaxNumberOfHopsRemaining,
                                    (byte)Engine.InsecureRandom.Next(10),
                                    directionVectorNullable,
                                    false
                                    );
                                _neighborhoodExtensionFailuresCountInArow = 0;
                            }
                        }
                    }                  
                    catch (Exception exc)
                    {
                        _neighborhoodExtensionFailuresCountInArow++;
                        if (exc is RequestRejectedException || exc is DrpTimeoutException)
                        {
                            var msg = $"failed to extend neighbors for {this}: {exc.Message}\r\n{_neighborhoodExtensionFailuresCountInArow} failures in a row";
                            if (_neighborhoodExtensionFailuresCountInArow < 3) Engine.WriteToLog_reg_requesterSide_higherLevelDetail(msg, null, null);
                            else if (_neighborhoodExtensionFailuresCountInArow < 5) Engine.WriteToLog_reg_requesterSide_lightPain(msg, null, null);
                            else Engine.WriteToLog_reg_requesterSide_mediumPain(msg, null, null);
                        }
                        else
                            Engine.WriteToLog_reg_requesterSide_mediumPain($"failed to extend neighbors for {this}: {exc}\r\n{_neighborhoodExtensionFailuresCountInArow} failures in a row", null, null);
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
            foreach (var neighbor in ConnectedNeighborsCanBeUsedForNewRequests)
            {
                if (this.Configuration.LocalPeerRegistrationId.Equals(neighbor.RemoteRegistrationId)) continue;

                var p2pConnectionValue_withNeighbor =
                    P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary,
                        this.Configuration.LocalPeerRegistrationId, this.ConnectedNeighborsBusySectorIds,
                        neighbor.RemoteRegistrationId, neighbor.RemoteNeighborsBusySectorIds ?? 0,
                        Engine.NumberOfDimensions,
                        true, 
                        this.AnotherNeighborToSameSectorExists(neighbor),
                        neighbor.Remote_AnotherNeighborToSameSectorExists ?? false, 
                        true
                        );
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
                var ping = worstNeighbor.CreatePing(false, true, 0, false);

                var pendingPingRequest = new PendingLowLevelUdpRequest("pendingPingRequest 351", worstNeighbor.RemoteEndpoint,
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
                }, "estroying worst P2P connection 2146");
            }
        }
        
        async Task TestDirections(DateTime timeNowUtc)
        {
            if (ConnectedNeighborsCanBeUsedForNewRequests.Count() >= _configuration.MinDesiredNumberOfNeighbors 
                && _configuration.AbsoluteMaxNumberOfNeighbors != 1) // also if not  special mode: mobile device connected only to 1 home device
            { // enough neighbors
                if (_latestEmptyDirectionsTestTimeUtc == null || timeNowUtc > _latestEmptyDirectionsTestTimeUtc.Value.AddSeconds(_configuration.TestDirectionsMinIntervalS))
                {
                    _latestEmptyDirectionsTestTimeUtc = timeNowUtc;
                    
                    var logger = new Logger(Engine, this, null, DrpPeerEngine.VisionChannelModuleName_p2p);

                    var numberOfDimensions = Engine.Configuration.SandboxModeOnly_NumberOfDimensions;
                    var vsic = new VectorSectorIndexCalculator(numberOfDimensions);
                    foreach (var directionVector in vsic.EnumerateDirections())
                        await TestDirection(logger, directionVector);

                }
            }
        }

        internal void EngineThreadOnTimer100ms(DateTime timeNowUtc)
        {
            try
            {
                _ = ConnectToNewNeighborAsync(timeNowUtc, false, null);
                NeighborsApoptosisProcedure(timeNowUtc);
                _ = TestDirections(timeNowUtc);
            }
            catch (Exception exc)
            {
                Engine.WriteToLog_p2p_mediumPain($"error in {this} timer procedure: {exc}");
            }
        }
        #endregion

        async Task BeginConnectToEPsAsync(IPEndPoint[] endpoints)// engine thread
        {
            foreach (var endpoint in endpoints)
            {
                try
                {
                    var conn = await Engine.RegisterAsync(this, endpoint, 0, 1, null, false); // engine thread
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
                }
                catch (Exception exc)
                {
                    Engine.HandleGeneralException("BeginConnectToEPs failed", exc);
                }
                cb();
            }, "BeginConnectToEPs4695");

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
        public int? MinDesiredNumberOfNeighbors = 12;
        public int? SoftMaxNumberOfNeighbors = 14; 
        public int? AbsoluteMaxNumberOfNeighbors = 18;
        public double? MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = 120;
        public double TestDirectionsMinIntervalS = 30;

        public static LocalDrpPeerConfiguration  Create(ICryptoLibrary cryptoLibrary, int? numberOfDimensions = null, byte[] ed25519privateKey = null, RegistrationId registrationId = null)
        {
            LocalDrpPeerConfiguration r;
            if (ed25519privateKey != null && registrationId != null)
            {
                r = new LocalDrpPeerConfiguration
                {
                    LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = ed25519privateKey },
                    LocalPeerRegistrationId = registrationId
                };
            }
            else
            {
                RegistrationId.CreateNew(cryptoLibrary, out var newPrivateKey, out var newRegistrationId);
                r = new LocalDrpPeerConfiguration
                {
                    LocalPeerRegistrationPrivateKey = newPrivateKey,
                    LocalPeerRegistrationId = newRegistrationId
                };
            }

            if (numberOfDimensions == 2)
            {
                r.MinDesiredNumberOfNeighbors = 5;
                r.SoftMaxNumberOfNeighbors = 7;
                r.AbsoluteMaxNumberOfNeighbors = 10;
            }
            return r;
        }
    }
}
