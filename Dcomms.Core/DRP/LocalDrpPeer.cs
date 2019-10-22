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
    public partial class LocalDrpPeer: IDisposable, IVisibleModule, IVisiblePeer
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
                    _cachedLocalPeerRegistrationIdVectorValues = RegistrationIdDistance.GetVectorValues(CryptoLibrary, Configuration.LocalPeerRegistrationId);
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
                foreach (var n in ConnectedNeighbors)
                    r |= n.SectorIndexFlagsMask;
                return r;
            }
        }

        #region IVisiblePeer implementation
        float[] IVisiblePeer.VectorValues => RegistrationIdDistance.GetVectorValues(CryptoLibrary, _configuration.LocalPeerRegistrationId).Select(x => (float)x).ToArray();
        bool IVisiblePeer.Highlighted => false;
        string IVisiblePeer.Name => Engine.Configuration.VisionChannelSourceId;
        IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => ConnectedNeighbors;
        string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer)
        {
            throw new NotImplementedException();
        }
        #endregion

        public IEnumerable<ConnectionToNeighbor> GetConnectedNeighborsForRouting(ConnectionToNeighbor sourceNeighborNullable,
            HashSet<ConnectionToNeighbor> alreadyTriedProxyingToDestinationPeersNullable,
            RegisterRequestPacket req)
        {
            foreach (var connectedPeer in ConnectedNeighbors.Where(x => x.PingReceived == true))
            {
                if (sourceNeighborNullable != null && connectedPeer == sourceNeighborNullable)
                {
                    Engine.WriteToLog_routing_detail($"skipping routing back to source peer {connectedPeer.RemoteRegistrationId}");
                    continue;
                }
                if (alreadyTriedProxyingToDestinationPeersNullable != null && alreadyTriedProxyingToDestinationPeersNullable.Contains(connectedPeer))
                {
                    Engine.WriteToLog_routing_detail($"skipping routing to previously tried peer {connectedPeer.RemoteRegistrationId}");
                    continue;
                }

                if (req.RequesterRegistrationId.Ed25519publicKey.Equals(connectedPeer.RemoteRegistrationId))
                {
                    Engine.WriteToLog_routing_detail($"skipping routing to peer with same regID {connectedPeer.RemoteRegistrationId}");
                    continue;
                }

                yield return connectedPeer;
            }
        }
        
        public void AddToConnectedNeighbors(ConnectionToNeighbor newConnectedNeighbor)
        {
            newConnectedNeighbor.OnP2pInitialized();
            ConnectedNeighbors.Add(newConnectedNeighbor);

            Engine.WriteToLog_p2p_higherLevelDetail(newConnectedNeighbor, $"added new connection to list of neighbors: {newConnectedNeighbor.RemoteRegistrationId} to {newConnectedNeighbor.RemoteEndpoint}");
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
                        if (this.Configuration.EntryPeerEndpoints != null && (Engine.InsecureRandom.NextDouble() < 0.03 || ConnectedNeighbors.Count == 0))
                        {
                            var epEndpoint = this.Configuration.EntryPeerEndpoints[Engine.InsecureRandom.Next(this.Configuration.EntryPeerEndpoints.Length)];
                            Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via EP {epEndpoint} ({ConnectedNeighbors.Count} connected neighbors now)");
                            await Engine.RegisterAsync(this, epEndpoint, 0, 20);
                        }
                        else
                        {
                            if (ConnectedNeighbors.Count != 0)
                            {
                                var neighborToSendRegister = ConnectedNeighbors[Engine.InsecureRandom.Next(ConnectedNeighbors.Count)];
                                Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via neighbor {neighborToSendRegister} ({ConnectedNeighbors.Count} connected neighbors now)");
                                await neighborToSendRegister.RegisterAsync(0, ConnectedNeighborsBusySectorIds, 20, 
                                    0//////////////////////////////////todo when need random hops??????????????? 2
                                    
                                    
                                    );
                            }
                        }
                    }
                    //catch (DrpResponderRejectedMaxHopsReachedException exc)
                    //{
                    //    Engine.WriteToLog_reg_requesterSide_lightPain($"failed to extend neighbors for {this}: {exc}.    adjusting  numberOfHops...");

                    //    if (_numberOfHopsToExtendNeighbors < 50)
                    //        _numberOfHopsToExtendNeighbors = (byte)(_numberOfHopsToExtendNeighbors + 5);
                    //}
                    catch (DrpResponderRejectedP2pNetworkServiceUnavailableException exc)
                    {
                        Engine.WriteToLog_reg_requesterSide_lightPain($"failed to extend neighbors for {this}: {exc}");
                    }
                    catch (Exception exc)
                    {
                        Engine.WriteToLog_reg_requesterSide_mediumPain($"failed to extend neighbors for {this}: {exc}");
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
            if (ConnectedNeighbors.Count > Configuration.AbsoluteMaxDesiredNumberOfNeighbors)
            {
                DestroyWorstNeighbor(null, timeNowUtc);
            }
            else if (ConnectedNeighbors.Count > Configuration.SoftMaxDesiredNumberOfNeighbors)
            {
                DestroyWorstNeighbor(P2pConnectionValueCalculator.MutualValueToKeepConnectionAlive_SoftLimitNeighborsCountCases, timeNowUtc);
            }
            else if (ConnectedNeighbors.Count >= Configuration.MinDesiredNumberOfNeighbors2)
            {
                if (_lastTimeDetroyedWorstNeighborUtc == null || (timeNowUtc - _lastTimeDetroyedWorstNeighborUtc.Value).TotalSeconds > Configuration.MinDesiredNumberOfNeighbors2Satisfied_WorstNeighborDestroyIntervalS)
                    DestroyWorstNeighbor(null, timeNowUtc);
            }
        }
        void DestroyWorstNeighbor(double? mutualValueLowLimit, DateTime timeNowUtc)
        {
            double? worstValue = mutualValueLowLimit;
            ConnectionToNeighbor worstNeighbor = null;
            foreach (var neighbor in  ConnectedNeighbors)
            {
                var p2pConnectionValue_withNeighbor =
                    P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, 
                        this.Configuration.LocalPeerRegistrationId, this.ConnectedNeighborsBusySectorIds,
                        neighbor.RemoteRegistrationId, neighbor.RemoteNeighborsBusySectorIds ?? 0);
                if (worstValue == null || p2pConnectionValue_withNeighbor < worstValue)
                {
                    worstValue = p2pConnectionValue_withNeighbor;
                    worstNeighbor = neighbor;
                }
            }

            if (worstNeighbor != null)
            {
                _lastTimeDetroyedWorstNeighborUtc = timeNowUtc;

                Engine.WriteToLog_p2p_higherLevelDetail(worstNeighbor, $"destroying worst P2P connection with neighbor. neighbors count = {ConnectedNeighbors.Count}");
                var ping = worstNeighbor.CreatePing(false, true, 0);
                
                var pendingPingRequest = new PendingLowLevelUdpRequest(worstNeighbor.RemoteEndpoint,
                                PongPacket.GetScanner(worstNeighbor.LocalNeighborToken32, ping.PingRequestId32), Engine.DateTimeNowUtc,
                                Engine.Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                                ping.Encode(),
                                Engine.Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS,
                                Engine.Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                            );

                worstNeighbor.Dispose();              
                _ = Engine.SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pong              
               

            }
        }

        internal void EngineThreadOnTimer100ms(DateTime timeNowUtc)
        {
            _ = ConnectToNewNeighborIfNeededAsync(timeNowUtc);
            NeighborsApoptosisProcedure(timeNowUtc);
        }
        #endregion
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
        public int? MinDesiredNumberOfNeighbors;
        public int? MinDesiredNumberOfNeighbors2 = 12;
        public int? SoftMaxDesiredNumberOfNeighbors = 13; 
        public int? AbsoluteMaxDesiredNumberOfNeighbors = 20;
        public double? MinDesiredNumberOfNeighbors2Satisfied_WorstNeighborDestroyIntervalS = 30;

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
