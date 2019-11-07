using Dcomms.Cryptography;
using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace Dcomms.Sandbox
{
    public class DrpTester4 : BaseNotify, IDisposable
    {
        const string VisionChannelModuleName = "drpDistanceTester";

        public class TestRunWithConfiguration: IVisibleModule
        {
            public int NumberOfPeers { get; set; } = 200;
            public int NumberOfEntryPeers { get; set; } = 5;
            public int NumberOfNeighbors_Min { get; set; } = 5; // min
            public int NumberOfNeighbors_SoftMax { get; set; } = 6; // target of apoptosis, if not painful
            public int NumberOfNeighbors_HardwareMax { get; set; } = 8; // absolute max, even if painful  (HW limit)
            public int NumberOfDimensions { get; set; } = 2; //8;
            public int OptimizationIterationsCount { get; set; } = 10;
            public bool UseGlobalSearchForRegistration { get; set; }
            public bool Consider2ndOrderNeighborsForInviteRouting { get; set; } = false;
            public int TotalMaxHopsCountToExtendNeighbors { get; set; } = 40;
            public int RandomHopsCountToExtendNeighbors { get; set; } = 10;
            public int NewTestedPeersCount { get; set; } = 100;
            public int TestedDestinationsCount { get; set; } = 10;
            public int TestedMaxHopsCount { get; set; } = 20;

            static int _testIdCounter = 0;
            public readonly int TestID = _testIdCounter++;
            public string VisionChannelSourceId => TestID.ToString();
            public string State { get; set; } = "created";

            public string Description => $"{NumberOfPeers}P, {NumberOfEntryPeers}EP, {NumberOfNeighbors_Min}..{NumberOfNeighbors_SoftMax}..{NumberOfNeighbors_HardwareMax}N," +
                $" {NumberOfDimensions}D, {OptimizationIterationsCount}OI, 2oN4IR={Consider2ndOrderNeighborsForInviteRouting}, RHCEN={RandomHopsCountToExtendNeighbors}, THCEN={TotalMaxHopsCountToExtendNeighbors}";
            public string Status => $"state:{State}- {Description}";
            public override string ToString() => Status;
            public readonly ICryptoLibrary CryptoLibrary = CryptoLibraries.Library;
            public readonly Random Rnd = new Random();

            public TestRunWithConfiguration Clone()
            {
                var dest = new TestRunWithConfiguration();
                var sourceProps = GetType().GetProperties().Where(x => x.CanRead).ToList();
                var destProps = GetType().GetProperties().Where(x => x.CanWrite).ToList();

                foreach (var sourceProp in sourceProps)
                {
                    if (destProps.Any(x => x.Name == sourceProp.Name))
                    {
                        var p = destProps.First(x => x.Name == sourceProp.Name);
                        if (p.CanWrite)
                        { // check if the property can be set or no.
                            p.SetValue(dest, sourceProp.GetValue(this, null), null);
                        }
                    }
                }
                return dest;
            }
        }
        public TestRunWithConfiguration Config { get; set; } = new TestRunWithConfiguration(); // not run, only config. instance
        public bool EnableDetailedLogs { get; set; }


        readonly VisionChannel _visionChannel;
        public DrpTester4(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }
        
        class Peer: IVisiblePeer
        {
            readonly string _name;
            public override string ToString() => _name;
            public string Name => _name;
            readonly TestRunWithConfiguration _tc;
            public readonly float[] VectorValues;
            public Peer(TestRunWithConfiguration tc, RegistrationId registrationId, string name)
            {
                _tc = tc;
                _name = name;
                RegistrationId = registrationId;
                VectorValues = RegistrationIdDistance.GetVectorValues(_tc.CryptoLibrary, registrationId, _tc.NumberOfDimensions).Select(x => (float)x).ToArray();                
            }
            P2pConnectionValueCalculator _p2pcvc;
            P2pConnectionValueCalculator P2PCVC
            {
                get
                {
                    if (_p2pcvc == null)
                        _p2pcvc = new P2pConnectionValueCalculator(this.VectorValues, Neighbors.Values.Select(x => x.VectorValues), null);                   
                    return _p2pcvc;
                }
            }
            public float GetMutualValue(Peer neighbor, bool neighborIsAlreadyConnected, bool considerValueOfUniqueSectors)
            {
                return P2PCVC.GetValue(neighbor.VectorValues, neighborIsAlreadyConnected, considerValueOfUniqueSectors) + neighbor.P2PCVC.GetValue(this.VectorValues, neighborIsAlreadyConnected, considerValueOfUniqueSectors);
            }

            void OnNeighborsChanged()
            {
                _p2pcvc = null;
            }
            public RegistrationId RegistrationId;
            public readonly Dictionary<RegistrationId, Peer> Neighbors = new Dictionary<RegistrationId, Peer>();
            public Peer GetMostFarNeighbor()
            {
                RegistrationIdDistance maxDistance = null;
                Peer mostFarNeighbor = null;
                foreach (var neighbor in Neighbors.Values)
                {
                    var d = neighbor.RegistrationId.GetDistanceTo(_tc.CryptoLibrary, this.RegistrationId, _tc.NumberOfDimensions);
                    if (maxDistance == null || d.IsGreaterThan(maxDistance))
                    {
                        maxDistance = d;
                        mostFarNeighbor = neighbor;
                    }
                }

                return mostFarNeighbor;
            }           
            IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => Neighbors.Values;

            float[] IVisiblePeer.VectorValues => VectorValues;

            public void AddNeighbor(Peer anotherPeer)
            {
                Neighbors.Add(anotherPeer.RegistrationId, anotherPeer);
                anotherPeer.Neighbors.Add(this.RegistrationId, this);
                OnNeighborsChanged();
                anotherPeer.OnNeighborsChanged();
            }
            public void RemoveP2pConnectionToNeighbor(Peer anotherPeer)
            {
                this.Neighbors.Remove(anotherPeer.RegistrationId);
                anotherPeer.Neighbors.Remove(this.RegistrationId);
                OnNeighborsChanged();
                anotherPeer.OnNeighborsChanged();
            }

            public RegistrationIdDistance GetMinimalDistanceOfThisAndNeighborsToTarget(Peer target)
            {
                RegistrationIdDistance minDistance = RegistrationId.GetDistanceTo(_tc.CryptoLibrary, target.RegistrationId, _tc.NumberOfDimensions);

                foreach (var neighbor in Neighbors.Values)
                {
                    var d = neighbor.RegistrationId.GetDistanceTo(_tc.CryptoLibrary, target.RegistrationId, _tc.NumberOfDimensions);
                    if (minDistance.IsGreaterThan(d))
                    {
                        minDistance = d;
                    }
                }

                return minDistance;
            }          
            string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer) => this.RegistrationId.GetDistanceTo(_tc.CryptoLibrary, ((Peer)toThisPeer).RegistrationId, _tc.NumberOfDimensions).ToString();

            public string GetP2pConnectionsPainSignal(Peer considerThisNeighborAsRemoved, Action<string> needMoreDetailsIfAllGood)
            {
                var p2pcvc = new P2pConnectionValueCalculator(this.VectorValues, Neighbors.Values.Where(x => x != considerThisNeighborAsRemoved).Select(x => x.VectorValues), needMoreDetailsIfAllGood);
               
                return p2pcvc.GetP2pConnectionsPainSignal(needMoreDetailsIfAllGood != null);
            }
            
            public bool Highlighted { get; set; }
        }
        public ICommand Test => new DelegateCommand(() =>
        {
            var a = new Action(() =>
            {
                var tc = Config.Clone();
                _visionChannel.RegisterVisibleModule(tc.VisionChannelSourceId, tc.TestID.ToString(), tc);
                _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"started test: id={tc.TestID}: {tc}");  
                
                var allPeers = new List<Peer>();
                _visionChannel.VisiblePeersDelegate = () => { return allPeers.Cast<IVisiblePeer>().ToList(); };

                #region entry peers
                tc.State = $"creating and connecting entry peers...";
                for (int i = 0; i < tc.NumberOfEntryPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(tc.CryptoLibrary);
                    allPeers.Add(new Peer(tc, peerConfig.LocalPeerRegistrationId, i.ToString()));
                }
                for (int i = 0; i < tc.NumberOfEntryPeers; i++)
                {
                    var peer = allPeers[i];
                    for (int j = 0; j < i; j++)                      
                    {
                        var anotherPeer = allPeers[j];
                        peer.AddNeighbor(anotherPeer);
                    }
                }
                #endregion

                for (int i = tc.NumberOfEntryPeers; i < tc.NumberOfPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(tc.CryptoLibrary);
                    var peer = new Peer(tc, peerConfig.LocalPeerRegistrationId, i.ToString());
                    allPeers.Add(peer);
                    tc.State = $"creating and connecting peers... peer {i}/{tc.NumberOfPeers}";
                    ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers(tc, allPeers);

                }
                _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"created and connected neighbors");
                P2pConnectionsQosVisionProcedure(tc, allPeers);

                #region optimize connections
                for (int i = 0; i < tc.OptimizationIterationsCount; i++)
                {
                    tc.State = $"optimizing connections... iteration {i}/{tc.OptimizationIterationsCount}";
                    OptimizeConnections_AllPeers(tc, allPeers);
                    if (EnableDetailedLogs || tc.NumberOfPeers <= 10000)
                        _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                           $"optimized connections, iteration {i}/{tc.OptimizationIterationsCount}:");
                    else
                        _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                           $"optimized connections, iteration {i}/{tc.OptimizationIterationsCount}");

                    P2pConnectionsQosVisionProcedure(tc, allPeers);
                }


                _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                   $"optimized connections:");
                #endregion

                _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"testing INVITE routing - existing peers");
                var rnd = new Random();    
                {
                    var numbersOfHops = new List<int>();
                    var successRates = new List<int>();
                    for (int i = 0; i < tc.TestedDestinationsCount; i++)
                    {
                        tc.State = $"testing INVITE... maxNumberOfHops={tc.TestedMaxHopsCount}, iteration{i}/{tc.TestedDestinationsCount}";

                        var sourcePeer = allPeers[rnd.Next(allPeers.Count)];
                    _retry:
                        var destinationPeer = allPeers[rnd.Next(allPeers.Count)];
                        if (destinationPeer == sourcePeer) goto _retry;

                        var routingSuccess = InviteRoutingProcedure(tc, 
                            sourcePeer, destinationPeer, tc.TestedMaxHopsCount, out var numberOfHops);
                        if (routingSuccess) numbersOfHops.Add(numberOfHops);
                        successRates.Add(routingSuccess ? 100 : 0);
                    }
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                        $"{tc.Description} existing peers: successRate = {successRates.Average()}%  avgHops={(numbersOfHops.Count != 0 ? numbersOfHops.Average() : -1)}, maxNumberOfHops={tc.TestedMaxHopsCount}");
                }


                {
                    var numbersOfHops = new List<int>();
                    var successRates = new List<int>();
                    for (int newTestedPeerI = 0; newTestedPeerI < tc.NewTestedPeersCount; newTestedPeerI++)
                    {
                        var newTestPeer = new Peer(tc,
                            LocalDrpPeerConfiguration.CreateWithNewKeypair(tc.CryptoLibrary).LocalPeerRegistrationId,
                            "test" + newTestedPeerI.ToString());
                        allPeers.Add(newTestPeer);
                        tc.State = $"creating and connecting test peer {newTestedPeerI}/{tc.NewTestedPeersCount}";
                        ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers(tc, allPeers);
                        P2pConnectionsQosVisionProcedure(tc, allPeers);

                        for (int i = 0; i < tc.TestedDestinationsCount; i++)
                        {
                           
                            var destinationPeer = allPeers[rnd.Next(allPeers.Count-1)];
                            var routingSuccess = InviteRoutingProcedure(tc,
                                newTestPeer, destinationPeer, tc.TestedMaxHopsCount, out var numberOfHops);
                            if (routingSuccess) numbersOfHops.Add(numberOfHops);
                            successRates.Add(routingSuccess ? 100 : 0);
                            if (!routingSuccess)
                            {
                                _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName,
                                    AttentionLevel.mediumPain,
                                    $"state of network when routing failed from {newTestPeer} to {destinationPeer}:");
                            }

                            var sourcePeer = allPeers[rnd.Next(allPeers.Count - 1)];
                            routingSuccess = InviteRoutingProcedure(tc,
                                sourcePeer, newTestPeer, tc.TestedMaxHopsCount, out numberOfHops);
                            if (routingSuccess) numbersOfHops.Add(numberOfHops);
                            successRates.Add(routingSuccess ? 100 : 0);
                            if (!routingSuccess)
                            {                              
                                _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName,
                                    AttentionLevel.mediumPain,
                                    $"state of network when routing failed from {sourcePeer} to {newTestPeer}:");                               
                            }

                            tc.State = $"tested INVITE... test peer {newTestedPeerI}/{tc.NewTestedPeersCount}, maxNumberOfHops={tc.TestedMaxHopsCount}, iteration{i}/{tc.TestedDestinationsCount} ...successRate={successRates.Average()}%";
                            
                        }


                        var neighborsBeforeDisconnection = newTestPeer.Neighbors.Values.ToList();
                        ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers(tc, allPeers);

                        var neighborsAfterRepair = newTestPeer.Neighbors.Values.ToList();
                        var staticNeighborsPercent = 100.0f * neighborsAfterRepair.Count(x => neighborsBeforeDisconnection.Contains(x)) / neighborsAfterRepair.Count;

                        _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, staticNeighborsPercent > 70 ? AttentionLevel.deepDetail : AttentionLevel.lightPain,
                                 $"{tc.Description} staticNeighborsPercent={staticNeighborsPercent}");


                    }
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.needsAttention,
                        $"{tc.Description} new peers: successRate={successRates.Average()}%  avgHops={(numbersOfHops.Count != 0 ? numbersOfHops.Average() : -1)}, maxNumberOfHops={tc.TestedMaxHopsCount}");
                }


              //  tc.State = $"ready";
            });
            a.BeginInvoke((ar) => a.EndInvoke(ar), null);
        });

        void OptimizeConnections_AllPeers(TestRunWithConfiguration tc, List<Peer> allPeers)
        {
            int ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers_N = Math.Max(79, allPeers.Count / 10);
            for (int j = tc.NumberOfEntryPeers; j < allPeers.Count; j++)
            {
                var peer = allPeers[j];
                               
                if (LetOneWorstP2pConnectionDie(tc, peer, false)) // destroy 1 worst connection
                {
                    // if (n % 159 == 0)  _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                    ConnectToNeighborsIfNotEnough_OnePeer(tc, peer, allPeers, tc.NumberOfNeighbors_Min); // add new connection
                }
                if (j % ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers_N == 0)
                    ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers(tc, allPeers);               
            }
            ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers(tc, allPeers);
                                   
        }

        void ConnectToNeighbors_Apoptosis_SelfHealing_AllPeers(TestRunWithConfiguration tc, List<Peer> allPeers)
        {
            for (int minNumberOfNeighbors_i = 1; minNumberOfNeighbors_i < tc.NumberOfNeighbors_Min; minNumberOfNeighbors_i++)
            {
                foreach (var peer in allPeers)
                {
                    ConnectToNeighborsIfNotEnough_OnePeer(tc, peer, allPeers, minNumberOfNeighbors_i);
                    for (int x = 0; x < 10; x++)
                    {
                        var painSignal = peer.GetP2pConnectionsPainSignal(null, null);
                        if (painSignal != null)
                        {
                            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                                               $"@ConnectToNeighbors_AllPeers healing connections of peer {peer}: {painSignal}  (iteration {x})");
                            ConnectToNeighborsIfNotEnough_OnePeer(tc, peer, allPeers, peer.Neighbors.Count + 1); // heal by making 1 more connection
                        }
                        else break;
                    }
                }
              
                NeighborsApoptosisProcedure_AllPeers(tc, allPeers);
                if (EnableDetailedLogs)
                    _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                       $"@ConnectToNeighbors_AllPeers after apoptosis:");
            }
        }
        bool ConnectToNeighborsIfNotEnough_OnePeer(TestRunWithConfiguration tc, Peer peer, List<Peer> allPeers, int minNumberOfNeighbors_i)
        {
            while (peer.Neighbors.Count < minNumberOfNeighbors_i)
            {
                int retryCounter = 0;
            _retry:
                var newNeighbor = ConnectToNewNeighbor(tc, peer, allPeers);
                if (newNeighbor == null)
                {
                    if (retryCounter++ < 10) goto _retry;
                    else return false;
                }

                if (EnableDetailedLogs)
                    _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                       $"connected peer {peer} to {newNeighbor}");
            }
            return true;
        }


        #region apoptosis
        void NeighborsApoptosisProcedure_AllPeers(TestRunWithConfiguration tc, List<Peer> allPeers)
        {
            foreach (var peer in allPeers)
                NeighborsApoptosisProcedure(tc, peer);
        }
        void NeighborsApoptosisProcedure(TestRunWithConfiguration tc, Peer peer)
        {
            while (peer.Neighbors.Count > tc.NumberOfNeighbors_HardwareMax)
                LetOneWorstP2pConnectionDie(tc, peer, true);

            int c = peer.Neighbors.Count - tc.NumberOfNeighbors_SoftMax;
            for (int i = 0; i < c; i++)
                if (!LetOneWorstP2pConnectionDie(tc, peer, false))
                    break;
        }
        bool LetOneWorstP2pConnectionDie(TestRunWithConfiguration tc, Peer peer, bool hardMaxNeighborsLimit)
        {
            if (peer.Neighbors.Count > 1)
            {
                float? leastMutualValue = null;
                Peer worstP2pConnectionToNeighbor = null;
                foreach (var neighbor in peer.Neighbors.Values)
                {
                    var painSignal = peer.GetP2pConnectionsPainSignal(neighbor, null);
                    if (painSignal != null && hardMaxNeighborsLimit == false) continue; // painful to delete this connection

                    var mutualValue = peer.GetMutualValue(neighbor, true, true);                    
                    if (EnableDetailedLogs)
                        _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"peer {peer}, neighbor={neighbor}, mutualValue={mutualValue}");
                    if (leastMutualValue == null || mutualValue < leastMutualValue)
                    {
                        worstP2pConnectionToNeighbor = neighbor;
                        leastMutualValue = mutualValue;
                    }
                }

                if (worstP2pConnectionToNeighbor != null)
                {
                    if (hardMaxNeighborsLimit == false)
                    {
                        // soft limit: dont destroy connection if it is too valuable
                        if (leastMutualValue > P2pConnectionValueCalculator.MutualValueToKeepConnectionAlive_SoftLimitNeighborsCountCases)
                        {
                            return false;
                        }
                    }


                    if (EnableDetailedLogs)
                        _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"P2P connection apoptosis: peer {peer}, worstP2pConnectionToNeighbor={worstP2pConnectionToNeighbor}");
                    peer.RemoveP2pConnectionToNeighbor(worstP2pConnectionToNeighbor);
                    return true;
                }
            }
            return false;
        }
        #endregion
        
        Peer ConnectToNewNeighbor(TestRunWithConfiguration tc, Peer peer, List<Peer> allPeers) // = registration procedure in DRP
        {
            if (tc.UseGlobalSearchForRegistration)
            {
                float? maxMutualValue = null;
                Peer bestNeighbor = null;
                
                foreach (var potentialNeighbor in allPeers)
                {
                    if (potentialNeighbor == peer) continue;
                    if (peer.Neighbors.ContainsKey(potentialNeighbor.RegistrationId)) continue;
                    var mutualValue = peer.GetMutualValue(potentialNeighbor, false, true);
                    if (maxMutualValue == null || mutualValue > maxMutualValue)
                    {
                        bestNeighbor = potentialNeighbor;
                        maxMutualValue = mutualValue;
                    }
                }

                if (EnableDetailedLogs)
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                                   $"global reg search mode: connecting peer={peer} to bestNeighbor={bestNeighbor}"
                                   //, allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers
                                   );

                if (bestNeighbor != null)
                {
                    peer.AddNeighbor(bestNeighbor);
                    return bestNeighbor;
                }
                else return null;
            }
            else
            {                         
                if (peer.Neighbors.Count == 0// || rnd.NextDouble() < 0.1
                    )
                {
                    var entryPeer = allPeers[tc.Rnd.Next(tc.NumberOfEntryPeers)];                    
                    RegisterRoutingProcedure(tc, peer, entryPeer, 0, false, out var newNeighbor);
                    if (newNeighbor != null)
                    {
                        peer.AddNeighbor(newNeighbor);
                        return newNeighbor;
                    }
                }
                else
                {
                    var entryNeighbor =// peer.GetMostFarNeighbor();                        
                        peer.Neighbors.Values.ToList()[tc.Rnd.Next(peer.Neighbors.Count)];

                    RegisterRoutingProcedure(tc, peer, entryNeighbor, tc.RandomHopsCountToExtendNeighbors, true, out var newNeighbor);
                    if (newNeighbor != null)
                    {
                        peer.AddNeighbor(newNeighbor);
                        return newNeighbor;
                    }
                }
            }
           
            return null;
        }

        /// <returns>true if reached toPeer via p2p conenctions</returns>
        bool InviteRoutingProcedure(TestRunWithConfiguration tc, Peer fromPeer, Peer toPeer, int maxNumberOfHops, out int numberOfHops)
        {
            numberOfHops = 0;

            var visiblePeers_RoutedPath = new List<IVisiblePeer>();
            int hopsRemaining = maxNumberOfHops;
            var currentPeer = fromPeer;
            visiblePeers_RoutedPath.Add(currentPeer);
            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"@InviteRoutingProcedure from {fromPeer.RegistrationId} to {toPeer.RegistrationId}");
            var avoidedPeers = new HashSet<Peer>();
            while (currentPeer != toPeer)
            {
                InviteRoutingIterationProcedure(tc, currentPeer, toPeer, avoidedPeers, out var bestNextPeer);
               
                numberOfHops++;
                hopsRemaining--;
                if (bestNextPeer == null)
                {
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...dead end");
                    break; 
                }
                currentPeer = bestNextPeer;
                avoidedPeers.Add(currentPeer);

                visiblePeers_RoutedPath.Add(currentPeer);

                if (hopsRemaining <= 0)
                {
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...number of hops reached zero");
                    break;
                }
            }
                       
            var success = currentPeer == toPeer;
            if (!success)
                visiblePeers_RoutedPath.Add(toPeer);

            _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName,
                success ? AttentionLevel.deepDetail : AttentionLevel.mediumPain,
                $"success={success}, numberOfHops={numberOfHops} of maxNumberOfHops={maxNumberOfHops}", visiblePeers_RoutedPath);
           

            return success;
        }                
        void RegisterRoutingProcedure(TestRunWithConfiguration tc, Peer registerForLocalMainPeer, Peer currentPeer, int randomHopsCount, bool considerValueOfUniqueSectors, out Peer closestNewNeighbor)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> RegisterRoutingProcedure() registerForMainPeer={registerForLocalMainPeer}, currentPeer={currentPeer}");
            closestNewNeighbor = null;

            int hopsRemaining = tc.TotalMaxHopsCountToExtendNeighbors;

            HashSet<Peer> avoidedPeers = new HashSet<Peer>();
            avoidedPeers.Add(registerForLocalMainPeer);
            foreach (var existingNeighb in registerForLocalMainPeer.Neighbors.Values) avoidedPeers.Add(existingNeighb);

            int randomHopsCountRemaining = randomHopsCount;
            while (hopsRemaining > 0)
            {
                RegisterRoutingIterationProcedure(tc, currentPeer, registerForLocalMainPeer, 
                    avoidedPeers, randomHopsCountRemaining > 0, considerValueOfUniqueSectors,
                    out var bestNextPeer, out var currentPeerIsBetterThanAnyNeighbor);
                if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"@RegisterRoutingProcedure() hopsRemaining={hopsRemaining} bestNextPeer={bestNextPeer} currentPeerIsBetterThanAnyNeighbor={currentPeerIsBetterThanAnyNeighbor}");
                                
                hopsRemaining--;
                randomHopsCountRemaining--;
                if (bestNextPeer == null) break;
                else
                {
                    if (currentPeerIsBetterThanAnyNeighbor)
                    {
                        if (!currentPeer.Neighbors.ContainsKey(registerForLocalMainPeer.RegistrationId))
                        {
                            closestNewNeighbor = currentPeer;
                            break;
                        }
                    }
                    avoidedPeers.Add(bestNextPeer);
                    currentPeer = bestNextPeer;
                    closestNewNeighbor = bestNextPeer;
                }
            }

            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $"<< RegisterRoutingProcedure() closestNewNeighbor={closestNewNeighbor}");
        }

        void RegisterRoutingIterationProcedure(TestRunWithConfiguration tc, Peer currentPeer, Peer destinationLocalPeer, HashSet<Peer> avoidedPeers,
            bool randomHop, bool considerValueOfUniqueSectors,
            out Peer bestNextPeer, out bool currentPeerIsBetterThanAnyNeighbor)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> RegisterRoutingIterationProcedure() currentPeer={currentPeer}, destinationLocalPeer={destinationLocalPeer}, randomHop={randomHop}");

            bestNextPeer = null;
            currentPeerIsBetterThanAnyNeighbor = false;

            if (randomHop)
            {
                var peersToSelectRandomly = new List<Peer>();
                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    if (avoidedPeers.Contains(nextPeer)) continue;
                    peersToSelectRandomly.Add(nextPeer);
                }

                if (peersToSelectRandomly.Count != 0)
                    bestNextPeer = peersToSelectRandomly[tc.Rnd.Next(peersToSelectRandomly.Count)];
                             
                if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"<< RegisterRoutingIterationProcedure() returns bestNextPeer={bestNextPeer}");
            }
            else
            {
                float? maxMutualValue = null;
                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    if (avoidedPeers.Contains(nextPeer)) continue;

                    var mutualValue = destinationLocalPeer.GetMutualValue(nextPeer, false, considerValueOfUniqueSectors);
                    if (maxMutualValue == null || mutualValue > maxMutualValue)
                    {
                        bestNextPeer = nextPeer;
                        maxMutualValue = mutualValue;
                        currentPeerIsBetterThanAnyNeighbor = false;           
                    }
                }

                {
                    var mutualValue = destinationLocalPeer.GetMutualValue(currentPeer, false, considerValueOfUniqueSectors);
                    if (maxMutualValue == null || mutualValue > maxMutualValue)
                        currentPeerIsBetterThanAnyNeighbor = true;
                  
                    if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        $"@ RoutingIterationProcedure() currentPeer={currentPeer} mutualValue={mutualValue}");
                }
                if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"<< RoutingIterationProcedure() returns bestNextPeer={bestNextPeer} maxMutualValue={maxMutualValue} currentPeerIsBetterThanAnyNeighbor={currentPeerIsBetterThanAnyNeighbor}");
            }         
        }
        
        void InviteRoutingIterationProcedure(TestRunWithConfiguration tc, Peer currentPeer, Peer destinationPeer, HashSet<Peer> avoidedPeers, out Peer bestNextPeer)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> InviteRoutingIterationProcedure() currentPeer={currentPeer}, destinationPeer={destinationPeer}");

            bestNextPeer = null;   
            RegistrationIdDistance minDistance = null;
            foreach (var nextPeer in currentPeer.Neighbors.Values)
            {
                // dont go back to source peer
                if (avoidedPeers.Contains(nextPeer)) continue;

                RegistrationIdDistance d;
                if (tc.Consider2ndOrderNeighborsForInviteRouting) d = nextPeer.GetMinimalDistanceOfThisAndNeighborsToTarget(destinationPeer);
                else d = nextPeer.RegistrationId.GetDistanceTo(tc.CryptoLibrary, destinationPeer.RegistrationId, tc.NumberOfDimensions);

                if (minDistance == null || minDistance.IsGreaterThan(d))
                {
                    bestNextPeer = nextPeer;
                    minDistance = d;
                }
            }                       
            if (EnableDetailedLogs) _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $"<< InviteRoutingIterationProcedure() returns bestNextPeer={bestNextPeer} minDistance={minDistance}");
        }

        void P2pConnectionsQosVisionProcedure(TestRunWithConfiguration tc, List<Peer> allPeers)
        {
            int painfulCount = 0;
            foreach (var peer in allPeers)
            {
                var painSignal = peer.GetP2pConnectionsPainSignal(null, null);
                if (painSignal != null)
                {
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.mediumPain,
                         $"peer {peer} bad connections: {painSignal}");
                    peer.Highlighted = true;
                    painfulCount++;
                }
                else
                    peer.Highlighted = false;

                if (EnableDetailedLogs)
                {
                    var wtl = new Action<string>((msg) =>
                    {
                        _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.detail, $"vision: peer{peer}: {msg}");
                    });
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.detail,
                         $"vision: peer {peer}: {peer.GetP2pConnectionsPainSignal(null, wtl)}");
                    _visionChannel.Emit(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.detail,
                         $"vision: peer {peer}: neighbors: {String.Join(";", peer.Neighbors.Values.Select(x => x.ToString()))}");
                }
            }

            var avgNeighborsCount = allPeers.Average(x => x.Neighbors.Count);
            var maxNeighborsCount = allPeers.Max(x => x.Neighbors.Count);


            // percent of peers with bad connections
            _visionChannel.EmitListOfPeers(tc.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                 $"bad connections peers percentage: {100.0 * painfulCount / allPeers.Count}%. avgNeighborsCount={avgNeighborsCount}, maxNeighborsCount={maxNeighborsCount}");
        }

        public void Dispose()
        {
        }
    }
}
