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
    public class DrpDistanceTester : BaseNotify, IDisposable
    {
        const string VisionChannelModuleName = "drpDistanceTester";
        const string VisionChannelSourceId = "test";
        public int NumberOfPeers { get; set; } = 20;
        public int NumberOfEntryPeers { get; set; } = 5;
        public int MinNumberOfNeighbors { get; set; } = 4;
        public int MaxNumberOfNeighbors { get; set; } = 5;
        public int NumberOfDimensions { get; set; } = 2;//8;
        public int OptimizationIterationsCount { get; set; } = 20;
        public bool EnableDetailedLogs { get; set; }
        public bool UseGlobalSearchForRegistration { get; set; }
        public bool Consider2ndOrderNeighborsForInviteRouting { get; set; } = false;

        public int TotalMaxHopsCountToExtendNeighbors { get; set; } = 40;
        public int RandomHopsCountToExtendNeighbors { get; set; } = 15;

        readonly VisionChannel _visionChannel;
        public DrpDistanceTester(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }
        
        class Peer: IVisiblePeer
        {
            readonly string _name;
            public override string ToString() => _name;
            ICryptoLibrary _cryptoLibrary;
            int _numberOfDimensions;
            public readonly float[] VectorValues;
            public Peer(ICryptoLibrary cryptoLibrary, RegistrationId registrationId, string name, int numberOfDimensions)
            {
                _numberOfDimensions = numberOfDimensions;
                _cryptoLibrary = cryptoLibrary;
                _name = name;
                RegistrationId = registrationId;
                VectorValues = RegistrationIdDistance.GetVectorValues(cryptoLibrary, registrationId, numberOfDimensions).Select(x => (float)x).ToArray();                
            }
            P2pConnectionValueCalculator _p2pcvc;
            P2pConnectionValueCalculator P2PVC
            {
                get
                {
                    if (_p2pcvc == null)
                        _p2pcvc = new P2pConnectionValueCalculator(this.VectorValues, Neighbors.Values.Select(x => x.VectorValues));                   
                    return _p2pcvc;
                }
            }
            public double GetMutualValue(Peer neighbor, bool neighborIsAlreadyConnected, bool considerValueOfUniqueSectors)
            {
                return P2PVC.GetValue(neighbor.VectorValues, neighborIsAlreadyConnected, considerValueOfUniqueSectors) + neighbor.P2PVC.GetValue(this.VectorValues, neighborIsAlreadyConnected, considerValueOfUniqueSectors);
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
                    var d = neighbor.RegistrationId.GetDistanceTo(_cryptoLibrary, this.RegistrationId, _numberOfDimensions);
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
                RegistrationIdDistance minDistance = RegistrationId.GetDistanceTo(_cryptoLibrary, target.RegistrationId, _numberOfDimensions);

                foreach (var neighbor in Neighbors.Values)
                {
                    var d = neighbor.RegistrationId.GetDistanceTo(_cryptoLibrary, target.RegistrationId, _numberOfDimensions);
                    if (minDistance.IsGreaterThan(d))
                    {
                        minDistance = d;
                    }
                }

                return minDistance;
            }          
            string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer) => this.RegistrationId.GetDistanceTo(_cryptoLibrary, ((Peer)toThisPeer).RegistrationId, _numberOfDimensions).ToString();
        }
        public ICommand Test => new DelegateCommand(() =>
        {
            var a = new Action(() =>
            {
                var numberOfPeers = NumberOfPeers;
                var optimizationIterationsCount = OptimizationIterationsCount;
                var numberOfEntryPeers = NumberOfEntryPeers;
                var numberOfDimensions = NumberOfDimensions;
                var minNumberOfNeighbors = MinNumberOfNeighbors;
                var maxNumberOfNeighbors = MaxNumberOfNeighbors;
                var cryptoLibrary = CryptoLibraries.Library;
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, 
                    $"creating and connecting peers... NumberOfPeers={numberOfPeers}, numberOfDimensions={numberOfDimensions}, numberOfEntryPeers={numberOfEntryPeers}, NumberOfNeighbors={minNumberOfNeighbors}..{maxNumberOfNeighbors}");
                

                var allPeers = new List<Peer>();

                #region entry peers
                for (int i = 0; i < numberOfEntryPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(cryptoLibrary);
                    allPeers.Add(new Peer(cryptoLibrary, peerConfig.LocalPeerRegistrationId, i.ToString(), numberOfDimensions));
                }
                for (int i = 0; i < numberOfEntryPeers; i++)
                {
                    var peer = allPeers[i];
                    for (int j = 0; j < i; j++)                      
                    {
                        var anotherPeer = allPeers[j];
                        peer.AddNeighbor(anotherPeer);
                    }
                }
                #endregion

                var rnd2 = new Random();
                for (int i = numberOfEntryPeers; i < numberOfPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(cryptoLibrary);
                    var peer = new Peer(cryptoLibrary, peerConfig.LocalPeerRegistrationId, i.ToString(), numberOfDimensions);
                    allPeers.Add(peer);
                    ConnectToNeighbors_AllPeers(rnd2, allPeers, minNumberOfNeighbors, maxNumberOfNeighbors, numberOfEntryPeers);        
                }
                _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                   $"created and connected neighbors: NumberOfNeighbors={minNumberOfNeighbors}..{maxNumberOfNeighbors}, peers count={allPeers.Count}", 
                                   allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);
                
                #region optimize connections
                for (int i = 0; i < optimizationIterationsCount; i++)
                {
                    for (int j = numberOfEntryPeers; j < allPeers.Count; j++)
                    {
                        var peer = allPeers[j];
                        LetOneWorstP2pConnectionDie(peer);
                        // if (n % 159 == 0)  _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                        ConnectToNeighbors(rnd2, peer, allPeers, minNumberOfNeighbors, numberOfEntryPeers);
                        if (j % 79 == 0)
                        {
                           // NeighborsApoptosisProcedure_AllPeers(cryptoLibrary, allPeers, maxNumberOfNeighbors, numberOfDimensions);
                            ConnectToNeighbors_AllPeers(rnd2, allPeers, minNumberOfNeighbors, maxNumberOfNeighbors, numberOfEntryPeers);
                        }
                    }
                    NeighborsApoptosisProcedure_AllPeers(allPeers, maxNumberOfNeighbors);

                    if (EnableDetailedLogs || numberOfPeers <= 10000)
                        _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                           $"optimized connections, iteration {i}/{optimizationIterationsCount}:", allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);
                    else
                        _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                           $"optimized connections, iteration {i}/{optimizationIterationsCount}");
                }

                _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                   $"optimized connections:", allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);
                #endregion

                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"testing INVITE routing");
                var rnd = new Random();
                for (int maxNumberOfHops = 10; maxNumberOfHops < 50; maxNumberOfHops += 10)
                {
                    var numbersOfHops = new List<int>();
                    var successRates = new List<int>();
                    for (int i = 0; i < 200; i++)
                    {
                        var sourcePeer = allPeers[rnd.Next(allPeers.Count)];
                    _retry:
                        var destinationPeer = allPeers[rnd.Next(allPeers.Count)];
                        if (destinationPeer == sourcePeer) goto _retry;

                        var routingSuccess = InviteRoutingProcedure($"numberOfDimensions={numberOfDimensions}, optimizationIterationsCount={optimizationIterationsCount}, NumberOfPeers={numberOfPeers}, NumberOfNeighbors={minNumberOfNeighbors}...{maxNumberOfNeighbors}",
                            cryptoLibrary, sourcePeer, destinationPeer, maxNumberOfHops, numberOfDimensions, out var numberOfHops);
                        if (routingSuccess) numbersOfHops.Add(numberOfHops);
                        successRates.Add(routingSuccess ? 100 : 0);
                    }
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                        $"success rate = {successRates.Average()}%  average number of hops = {(numbersOfHops.Count != 0 ? numbersOfHops.Average() : -1)}" +
                        $"   optimizationIterationsCount={optimizationIterationsCount}, maxNumberOfHops={maxNumberOfHops}, " +
                        $"numberOfPeers={numberOfPeers}, NumberOfNeighbors={minNumberOfNeighbors}..{maxNumberOfNeighbors}");
                }
            });
            a.BeginInvoke((ar) => a.EndInvoke(ar), null);
        });
        void ConnectToNeighbors_AllPeers(Random rnd, List<Peer> allPeers, int minNumberOfNeighbors, int maxNumberOfNeighbors, int numberOfEntryPeers)
        {
            for (int minNumberOfNeighbors_i = 1; minNumberOfNeighbors_i < minNumberOfNeighbors; minNumberOfNeighbors_i++)
            {
                foreach (var peer in allPeers)
                    ConnectToNeighbors(rnd, peer, allPeers, minNumberOfNeighbors_i, numberOfEntryPeers); 
              
                NeighborsApoptosisProcedure_AllPeers(allPeers, maxNumberOfNeighbors);
                if (EnableDetailedLogs)
                    _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                       $"@ConnectToNeighbors_AllPeers after apoptosis:", allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);
            }
        }
        bool ConnectToNeighbors(Random rnd, Peer peer, List<Peer> allPeers, int minNumberOfNeighbors, int numberOfEntryPeers)
        {
            while (peer.Neighbors.Count < minNumberOfNeighbors)
            {
                int retryCounter = 0;
            _retry:
                var newNeighbor = ConnectToNewNeighbor(peer, allPeers, rnd, numberOfEntryPeers);
                if (newNeighbor == null)
                {
                    if (retryCounter++ < 10) goto _retry;
                    else return false;
                }

                if (EnableDetailedLogs)
                    _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                       $"connected peer {peer} to {newNeighbor}, minNumberOfNeighbors={minNumberOfNeighbors}", allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);
            }
            return true;
        }
        void LetOneWorstP2pConnectionDie(Peer peer)
        {
            if (peer.Neighbors.Count > 1)
            {
                double? leastMutualValue = null;
                Peer worstP2pConnectionToNeighbor = null;
                foreach (var neighbor in peer.Neighbors.Values)
                {
                    var mutualValue = peer.GetMutualValue(neighbor, true, true);
                    
                    if (EnableDetailedLogs)
                        _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                             $"peer {peer}, neighbor={neighbor}, mutualValue={mutualValue}");
                    if (leastMutualValue == null || mutualValue < leastMutualValue)
                    {
                        worstP2pConnectionToNeighbor = neighbor;
                        leastMutualValue = mutualValue;
                    }
                }

                if (worstP2pConnectionToNeighbor != null)
                {
                    if (EnableDetailedLogs)
                        _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                                       $"P2P connection apoptosis: peer {peer}, worstP2pConnectionToNeighbor={worstP2pConnectionToNeighbor}");
                    peer.RemoveP2pConnectionToNeighbor(worstP2pConnectionToNeighbor);
                }
            }
        }
        void NeighborsApoptosisProcedure_AllPeers(List<Peer> allPeers, int maxNumberOfNeighbors)
        {
            foreach (var peer in allPeers)
                NeighborsApoptosisProcedure(peer, maxNumberOfNeighbors);
        }
        void NeighborsApoptosisProcedure(Peer peer, int maxNumberOfNeighbors)
        {
            while (peer.Neighbors.Count > maxNumberOfNeighbors)
            {
                LetOneWorstP2pConnectionDie(peer);
            }
        }
        Peer ConnectToNewNeighbor(Peer peer, List<Peer> allPeers, Random rnd, int numberOfEntryPeers) // = registration procedure in DRP
        {
            if (UseGlobalSearchForRegistration)
            {
                double? maxMutualValue = null;
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
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
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
                    var entryPeer = allPeers[rnd.Next(numberOfEntryPeers)];                    
                    RegisterRoutingProcedure(peer, entryPeer, 0, rnd, false, out Peer newNeighbor);
                    if (newNeighbor != null)
                    {
                        peer.AddNeighbor(newNeighbor);
                        return newNeighbor;
                    }
                }
                else
                {
                    var entryNeighbor =// peer.GetMostFarNeighbor();                        
                        peer.Neighbors.Values.ToList()[rnd.Next(peer.Neighbors.Count)];

                    RegisterRoutingProcedure(peer, entryNeighbor, RandomHopsCountToExtendNeighbors, rnd, true, out var newNeighbor);

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
        bool InviteRoutingProcedure(string description, ICryptoLibrary cryptoLibrary, Peer fromPeer, Peer toPeer, int maxNumberOfHops, int numberOfDimensions, out int numberOfHops)
        {
            numberOfHops = 0;

            var visiblePeers = new List<IVisiblePeer>();
            int hopsRemaining = maxNumberOfHops;
            var currentPeer = fromPeer;
            visiblePeers.Add(currentPeer);
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"@InviteRoutingProcedure from {fromPeer.RegistrationId} to {toPeer.RegistrationId}");
            var avoidedPeers = new HashSet<Peer>();
            while (currentPeer != toPeer)
            {
                InviteRoutingIterationProcedure(cryptoLibrary, currentPeer, toPeer, avoidedPeers, numberOfDimensions, Consider2ndOrderNeighborsForInviteRouting, out var bestNextPeer);
               
                numberOfHops++;
                hopsRemaining--;
                if (bestNextPeer == null)
                {
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...dead end");
                    description += " - dead end";
                    break; 
                }
                currentPeer = bestNextPeer;
                avoidedPeers.Add(currentPeer);

                visiblePeers.Add(currentPeer);

                if (hopsRemaining <= 0)
                {
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...number of hops reached zero");
                    description += " - maxNhops";
                    break;
                }
            }
                       
            var success = currentPeer == toPeer;
            if (!success)
                visiblePeers.Add(toPeer);

            _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $"{description}: success={success}, numberOfHops={numberOfHops}", visiblePeers, VisiblePeersDisplayMode.routingPath);

            return success;
        }                
        void RegisterRoutingProcedure(Peer registerForLocalMainPeer, Peer currentPeer, int randomHopsCount, Random rnd, bool considerValueOfUniqueSectors, out Peer closestNewNeighbor)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> RegisterRoutingProcedure() registerForMainPeer={registerForLocalMainPeer}, currentPeer={currentPeer}");
            closestNewNeighbor = null;

            int hopsRemaining = TotalMaxHopsCountToExtendNeighbors;

            HashSet<Peer> avoidedPeers = new HashSet<Peer>();
            avoidedPeers.Add(registerForLocalMainPeer);
            foreach (var existingNeighb in registerForLocalMainPeer.Neighbors.Values) avoidedPeers.Add(existingNeighb);

            int randomHopsCountRemaining = randomHopsCount;
            while (hopsRemaining > 0)
            {
                RegisterRoutingIterationProcedure(currentPeer, registerForLocalMainPeer, 
                    avoidedPeers, randomHopsCountRemaining > 0, rnd, considerValueOfUniqueSectors,
                    out var bestNextPeer, out var currentPeerIsBetterThanAnyNeighbor);
                if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
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

            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $"<< RegisterRoutingProcedure() closestNewNeighbor={closestNewNeighbor}");
        }

        void RegisterRoutingIterationProcedure(Peer currentPeer, Peer destinationLocalPeer, HashSet<Peer> avoidedPeers,
            bool randomHop, Random rnd, bool considerValueOfUniqueSectors,
            out Peer bestNextPeer, out bool currentPeerIsBetterThanAnyNeighbor)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
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
                    bestNextPeer = peersToSelectRandomly[rnd.Next(peersToSelectRandomly.Count)];
                             
                if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"<< RegisterRoutingIterationProcedure() returns bestNextPeer={bestNextPeer}");
            }
            else
            {
                double? maxMutualValue = null;
                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    if (avoidedPeers.Contains(nextPeer)) continue;

                    var mutualValue = destinationLocalPeer.GetMutualValue(nextPeer, false, considerValueOfUniqueSectors);
                    if (maxMutualValue == null || mutualValue > maxMutualValue)
                    {
                        bestNextPeer = nextPeer;
                        maxMutualValue = mutualValue;
                        currentPeerIsBetterThanAnyNeighbor = false;
                        //    if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        //        $"@ RoutingIterationProcedure() peer={nextPeer} d={d}");
                    }
                }

                {
                    var mutualValue = destinationLocalPeer.GetMutualValue(currentPeer, false, considerValueOfUniqueSectors);
                    if (maxMutualValue == null || mutualValue > maxMutualValue)
                        currentPeerIsBetterThanAnyNeighbor = true;
                  
                    if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        $"@ RoutingIterationProcedure() currentPeer={currentPeer} mutualValue={mutualValue}");
                }
                if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"<< RoutingIterationProcedure() returns bestNextPeer={bestNextPeer} maxMutualValue={maxMutualValue} currentPeerIsBetterThanAnyNeighbor={currentPeerIsBetterThanAnyNeighbor}");
            }         
        }


        void InviteRoutingIterationProcedure(ICryptoLibrary cryptoLibrary, Peer currentPeer, Peer destinationPeer, HashSet<Peer> avoidedPeers, int numberOfDimensions,
            bool considerNeighborsOfNeighbors, out Peer bestNextPeer)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> InviteRoutingIterationProcedure() currentPeer={currentPeer}, destinationPeer={destinationPeer}, considerNeighborsOfNeighbors={considerNeighborsOfNeighbors}");

            bestNextPeer = null;   
            RegistrationIdDistance minDistance = null;
            foreach (var nextPeer in currentPeer.Neighbors.Values)
            {
                // dont go back to source peer
                if (avoidedPeers.Contains(nextPeer)) continue;

                RegistrationIdDistance d;
                if (considerNeighborsOfNeighbors) d = nextPeer.GetMinimalDistanceOfThisAndNeighborsToTarget(destinationPeer);
                else d = nextPeer.RegistrationId.GetDistanceTo(cryptoLibrary, destinationPeer.RegistrationId, numberOfDimensions);

                if (minDistance == null || minDistance.IsGreaterThan(d))
                {
                    bestNextPeer = nextPeer;
                    minDistance = d;
                }
            }
                       
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $"<< InviteRoutingIterationProcedure() returns bestNextPeer={bestNextPeer} minDistance={minDistance}");
        }
        
        public void Dispose()
        {
        }
    }
}
