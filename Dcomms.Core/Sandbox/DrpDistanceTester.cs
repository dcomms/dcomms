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
        public int NumberOfPeers { get; set; } = 150;
        public int NumberOfEntryPeers { get; set; } = 5;
        public int MinNumberOfNeighbors { get; set; } = 4;
        public int MaxNumberOfNeighbors { get; set; } = 6;
        public int NumberOfDimensions { get; set; } = 2;//8;
        public int ApoptosisIterationsCount { get; set; } = 10;
        public bool EnableDetailedLogs { get; set; }
        public bool UseGlobalSearchForRegistration { get; set; }
        public bool ConsiderNeighborsOfNeighborsForInviteRouting { get; set; }

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
            public Peer(ICryptoLibrary cryptoLibrary, RegistrationId registrationId, string name, int numberOfDimensions)
            {
                _numberOfDimensions = numberOfDimensions;
                _cryptoLibrary = cryptoLibrary;
                _name = name;
                RegistrationId = registrationId;
                VectorValues = RegistrationIdDistance.GetVectorValues(cryptoLibrary, registrationId, numberOfDimensions);
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
            public double[] VectorValues { get; private set; }             
            IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => Neighbors.Values;            
            public void AddNeighbor(Peer anotherPeer)
            {
                Neighbors.Add(anotherPeer.RegistrationId, anotherPeer);
                anotherPeer.Neighbors.Add(this.RegistrationId, this);
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
            public double GetMaxValueOfThisAndNeighbors(P2pConnectionValueCalculator p2pcvc)
            {
                var maxValue = p2pcvc.GetValue(this.RegistrationId);

                foreach (var neighbor in Neighbors.Values)
                {
                    var value = p2pcvc.GetValue(neighbor.RegistrationId);
                    if (value > maxValue)
                    {
                        maxValue = value;
                    }
                }

                return maxValue;
            }
            string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer) => this.RegistrationId.GetDistanceTo(_cryptoLibrary, ((Peer)toThisPeer).RegistrationId, _numberOfDimensions).ToString();
        }
        public ICommand Test => new DelegateCommand(() =>
        {
            var a = new Action(() =>
            {
                var numberOfPeers = NumberOfPeers;
                var apoptosisIterationsCount = ApoptosisIterationsCount;
                var numberOfEntryPeers = NumberOfEntryPeers;
                var numberOfDimensions = NumberOfDimensions;
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"creating peers... NumberOfPeers={numberOfPeers}, " +
                    $"numberOfDimensions={numberOfDimensions}, numberOfEntryPeers={numberOfEntryPeers}");


                var minNumberOfNeighbors = MinNumberOfNeighbors;
                var maxNumberOfNeighbors = MaxNumberOfNeighbors;


                var cryptoLibrary = CryptoLibraries.Library;

                var allPeers = new List<Peer>();
                for (int i = 0; i < numberOfPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(cryptoLibrary);
                    allPeers.Add(new Peer(cryptoLibrary, peerConfig.LocalPeerRegistrationId, i.ToString(), numberOfDimensions));
                }
                               
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"connecting neighbors... NumberOfPeers={numberOfPeers}, " +
                    $"NumberOfNeighbors={minNumberOfNeighbors}..{maxNumberOfNeighbors}");
                
                for (int i = 0; i < numberOfEntryPeers; i++)
                {
                    var peer = allPeers[i];

                    for (int j = 0; j < i; j++)                      
                    {
                        var anotherPeer = allPeers[j];
                        peer.AddNeighbor(anotherPeer);
                    }
                }

              //  int numberOfThreads = 1;// 6;
              //  var waitHandles = new List<EventWaitHandle>();
              //  for (int i = 0; i < numberOfThreads; i++)
              //      waitHandles.Add(new EventWaitHandle(false, EventResetMode.AutoReset));

              //  var connectA = new Action<int, EventWaitHandle>((threadId, wh) =>
              //  {
                var rnd2 = new Random();
                for (int minNumberOfNeighborsI = 3; minNumberOfNeighborsI < minNumberOfNeighbors; minNumberOfNeighborsI++)
                {
                    ConnectToNeighbors_AllPeers(rnd2, cryptoLibrary, allPeers, minNumberOfNeighborsI, maxNumberOfNeighbors, numberOfEntryPeers, numberOfDimensions);                   
                }

                if (EnableDetailedLogs) _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                   $"connected neighbors: NumberOfNeighbors={minNumberOfNeighbors}..{maxNumberOfNeighbors}, peers count= {allPeers.Count} ...", 
                                   allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);


                // apoptosis
                for (int i = 0; i < apoptosisIterationsCount; i++)
                {
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"connecting neighbors... apoptosis+interconnection... iteration {i}/{apoptosisIterationsCount}");
                    for (int j = numberOfEntryPeers; j < allPeers.Count; j++)
                    {
                        var peer = allPeers[j];
                        NeighborsApoptosisProcedure(cryptoLibrary, peer, numberOfDimensions);
                        // if (n % 159 == 0)  _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                        ConnectToNeighbors(rnd2, cryptoLibrary, peer, allPeers, minNumberOfNeighbors, numberOfEntryPeers, numberOfDimensions);
                        if (j % 79 == 0)
                        {
                           // NeighborsApoptosisProcedure_AllPeers(cryptoLibrary, allPeers, maxNumberOfNeighbors, numberOfDimensions);
                            ConnectToNeighbors_AllPeers(rnd2, cryptoLibrary, allPeers, minNumberOfNeighbors, maxNumberOfNeighbors, numberOfEntryPeers, numberOfDimensions);
                        }
                    }
                    NeighborsApoptosisProcedure_AllPeers(cryptoLibrary, allPeers, maxNumberOfNeighbors, numberOfDimensions);

                    _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                       $"apoptosis  iteration {i}/{apoptosisIterationsCount}:", allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);
                }

                _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                                   $"after apoptosis:", allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);


                //    wh.Set();
                //  });
                //   for (int i = 0; i < numberOfThreads; i++)
                //      connectA.BeginInvoke(i, waitHandles[i], (ar) => connectA.EndInvoke(ar), null);

                //    WaitHandle.WaitAll(waitHandles.ToArray());
                //    foreach (var wh in waitHandles) wh.Dispose();

                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...connected neighbors");

                //for (int i = 0; i < allPeers.Count; i++)
                //{
                 //   var peer = allPeers[i];
                 //   _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"{i}: {peer.Neighbors.Count} neighbors");
               // }


                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"testing number of hops");
                var rnd = new Random();
                for (int maxNumberOfHops = 10; maxNumberOfHops < 120; maxNumberOfHops += 10)
                {

                    var numbersOfHops = new List<int>();
                    var successRates = new List<int>();
                    for (int i = 0; i < 200; i++)
                    {
                        var sourcePeer = allPeers[rnd.Next(allPeers.Count)];
                    _retry:
                        var destinationPeer = allPeers[rnd.Next(allPeers.Count)];
                        if (destinationPeer == sourcePeer) goto _retry;

                        var routingSuccess = InviteRoutingProcedure($"numberOfDimensions={numberOfDimensions}, apoptosisIterationsCount={apoptosisIterationsCount}, NumberOfPeers={numberOfPeers}, NumberOfNeighbors={minNumberOfNeighbors}...{maxNumberOfNeighbors}",
                            cryptoLibrary, sourcePeer, destinationPeer, maxNumberOfHops, numberOfDimensions, out var numberOfHops);
                        if (routingSuccess) numbersOfHops.Add(numberOfHops);
                        successRates.Add(routingSuccess ? 100 : 0);
                    }
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                        $"success rate = {successRates.Average()}%  average number of hops = {(numbersOfHops.Count != 0 ? numbersOfHops.Average() : -1)}" +
                        $"   apoptosisIterationsCount={apoptosisIterationsCount}, maxNumberOfHops={maxNumberOfHops}, " +
                        $"numberOfPeers={numberOfPeers}, NumberOfNeighbors={minNumberOfNeighbors}..{maxNumberOfNeighbors}");
                }

            });
            a.BeginInvoke((ar) => a.EndInvoke(ar), null);
        });
        void ConnectToNeighbors_AllPeers(Random rnd, ICryptoLibrary cryptoLibrary, List<Peer> allPeers, int minNumberOfNeighbors, int maxNumberOfNeighbors, int numberOfEntryPeers, int numberOfDimensions)
        {
            for (int i = numberOfEntryPeers; i < allPeers.Count; i++)
            {
                var peer = allPeers[i];
                ConnectToNeighbors(rnd, cryptoLibrary, peer, allPeers, minNumberOfNeighbors, numberOfEntryPeers, numberOfDimensions);

                // if (c++ % 2 == 0)
                {
                    //   _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    //           $"connecting neighbors: minNumberOfNeighborsI={minNumberOfNeighborsI}/{minNumberOfNeighbors}, peer {i}/{allPeers.Count} ...");

                 //   if (EnableDetailedLogs) _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                 //            $"connecting neighbors: minNumberOfNeighbors={minNumberOfNeighborsI}/{minNumberOfNeighbors90}, peer {i}/{allPeers.Count} ...",
                 //            allPeers.Cast<IVisiblePeer>().ToList(), VisiblePeersDisplayMode.allPeers);

                }
            }
            NeighborsApoptosisProcedure_AllPeers(cryptoLibrary, allPeers, maxNumberOfNeighbors, numberOfDimensions);
        }
        bool ConnectToNeighbors(Random rnd, ICryptoLibrary cryptoLibrary, Peer peer, List<Peer> allPeers, int minNumberOfNeighbors, int numberOfEntryPeers, int numberOfDimensions)
        {
            while (peer.Neighbors.Count < minNumberOfNeighbors)
            {
                int retryCounter = 0;
              _retry:

                if (!ConnectToNewNeighbor(cryptoLibrary, peer, allPeers, rnd, numberOfEntryPeers, numberOfDimensions))
                {
                    if (retryCounter++ < 10) goto _retry;
                    else return false;
                }
            }
            return true;
        }
        void NeighborsApoptosisProcedure(ICryptoLibrary cryptoLibrary, Peer peer, int numberOfDimensions)
        {
            if (peer.Neighbors.Count > 1)
            {
                double? leastValue = null;
                Peer worstNeighbor = null;
                foreach (var neighbor in peer.Neighbors.Values)
                {
                    var p2pcvc = new P2pConnectionValueCalculator(peer.RegistrationId, cryptoLibrary, numberOfDimensions, peer.Neighbors.Keys.Where(x => x.Equals(neighbor.RegistrationId) == false));
                    var value = p2pcvc.GetValue(neighbor.RegistrationId);//    neighborRegistrationId.GetDistanceTo(cryptoLibrary, peer.RegistrationId, numberOfDimensions);

                    if (EnableDetailedLogs)
                        _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                                       $"peer {peer}, neighbor={neighbor}   value={value}  p2pcvc={p2pcvc.Description}");

                    if (leastValue == null || value < leastValue)
                    {
                        worstNeighbor = neighbor;
                        leastValue = value;
                    }
                }

                if (worstNeighbor != null)
                {
                    if (EnableDetailedLogs)
                        _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                                       $"apoptosis of neighbors: peer {peer}, worstNeighbor={worstNeighbor}");
                    peer.Neighbors.Remove(worstNeighbor.RegistrationId);
                    worstNeighbor.Neighbors.Remove(peer.RegistrationId);
                }
            }
        }
        void NeighborsApoptosisProcedure_AllPeers(ICryptoLibrary cryptoLibrary, List<Peer> allPeers, int maxNumberOfNeighbors, int numberOfDimensions)
        {
            foreach (var peer in allPeers)
                NeighborsApoptosisProcedure(cryptoLibrary, peer, maxNumberOfNeighbors, numberOfDimensions);
        }
        void NeighborsApoptosisProcedure(ICryptoLibrary cryptoLibrary, Peer peer, int maxNumberOfNeighbors, int numberOfDimensions)
        {
            while (peer.Neighbors.Count > maxNumberOfNeighbors)
            {
                NeighborsApoptosisProcedure(cryptoLibrary, peer, numberOfDimensions);
            }
        }
        bool ConnectToNewNeighbor(ICryptoLibrary cryptoLibrary, Peer peer, List<Peer> allPeers, Random rnd, int numberOfEntryPeers, int numberOfDimensions) // = registration procedure in DRP
        {
            if (UseGlobalSearchForRegistration)
            {
                RegistrationIdDistance minDistance = null;
                Peer newNeighbor = null;
                foreach (var potentialNeighbor in allPeers)
                {
                    if (potentialNeighbor == peer) continue;
                    if (peer.Neighbors.ContainsKey(potentialNeighbor.RegistrationId)) continue;
                    var d = potentialNeighbor.RegistrationId.GetDistanceTo(cryptoLibrary, peer.RegistrationId, numberOfDimensions);
                    if (minDistance == null || minDistance.IsGreaterThan(d))
                    {
                        newNeighbor = potentialNeighbor;
                        minDistance = d;
                    }
                }

                if (newNeighbor != null)
                {
                    peer.AddNeighbor(newNeighbor);
                    return true;
                }
                else return false;
            }
            else
            {
                P2pConnectionValueCalculator p2pcvc = null;                
                if (peer.Neighbors.Count != 0)
                    p2pcvc = new P2pConnectionValueCalculator(peer.RegistrationId, cryptoLibrary, numberOfDimensions, peer.Neighbors.Keys);
                               
                if (peer.Neighbors.Count == 0// || rnd.NextDouble() < 0.1
                    )
                {
                    var entryPeer = allPeers[rnd.Next(numberOfEntryPeers)];                    
                    RegisterRoutingProcedure(cryptoLibrary, peer, entryPeer, numberOfDimensions, p2pcvc, out Peer newNeighbor);
                    if (newNeighbor != null)
                    {
                        peer.AddNeighbor(newNeighbor);
                        return true;
                    }
                }
                else
                {
                    var entryNeighbor =// peer.GetMostFarNeighbor();                        
                        peer.Neighbors.Values.ToList()[rnd.Next(peer.Neighbors.Count)];

                    RegisterRoutingProcedure(cryptoLibrary, peer, entryNeighbor, numberOfDimensions, p2pcvc, out var newNeighbor);

                    if (newNeighbor != null)
                    {
                        peer.AddNeighbor(newNeighbor);
                        return true;
                    }
                }
            }
           
            return false;
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
                RoutingIterationProcedure(cryptoLibrary, currentPeer, toPeer, avoidedPeers, numberOfDimensions, ConsiderNeighborsOfNeighborsForInviteRouting, null, out var bestNextPeer, out var currentPeerIsBetterThanAnyNeighbor);
               
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
        void RegisterRoutingProcedure(ICryptoLibrary cryptoLibrary, Peer registerForMainPeer, Peer currentPeer, int numberOfDimensions, P2pConnectionValueCalculator p2pcvc, out Peer closestNewNeighbor)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> RegisterRoutingProcedure() registerForMainPeer={registerForMainPeer}, currentPeer={currentPeer}");
            closestNewNeighbor = null;

            int hopsRemaining = 30;

            HashSet<Peer> avoidedPeers = new HashSet<Peer>();
            avoidedPeers.Add(registerForMainPeer);
            foreach (var neighb in registerForMainPeer.Neighbors.Values) avoidedPeers.Add(neighb);

            while (hopsRemaining > 0)
            {
                RoutingIterationProcedure(cryptoLibrary, currentPeer, registerForMainPeer, 
                    avoidedPeers, numberOfDimensions, false,
                    p2pcvc,
                    out var bestNextPeer, out var currentPeerIsBetterThanAnyNeighbor);
                if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"@RegisterRoutingProcedure() hopsRemaining={hopsRemaining} bestNextPeer={bestNextPeer} currentPeerIsBetterThanAnyNeighbor={currentPeerIsBetterThanAnyNeighbor}");

                
                hopsRemaining--;
                if (bestNextPeer == null) break;
                else
                {
                    if (currentPeerIsBetterThanAnyNeighbor)
                    {
                        if (!currentPeer.Neighbors.ContainsKey(registerForMainPeer.RegistrationId))
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
        void RoutingIterationProcedure(ICryptoLibrary cryptoLibrary, Peer currentPeer, Peer destinationMainPeer, HashSet<Peer> avoidedPeers, int numberOfDimensions,
            bool considerNeighborsOfNeighbors, P2pConnectionValueCalculator p2pcvc,
            out Peer bestNextPeer, out bool currentPeerIsBetterThanAnyNeighbor)
        {
            if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                $">> RoutingIterationProcedure() currentPeer={currentPeer} destinationPeer={destinationMainPeer} considerNeighborsOfNeighbors={considerNeighborsOfNeighbors}");

            bestNextPeer = null;
            currentPeerIsBetterThanAnyNeighbor = false;

            if (p2pcvc == null)
            {
                RegistrationIdDistance minDistance = null;

                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    // dont go back to source peer
                    if (avoidedPeers.Contains(nextPeer)) continue;

                    RegistrationIdDistance d;
                    if (considerNeighborsOfNeighbors) d = nextPeer.GetMinimalDistanceOfThisAndNeighborsToTarget(destinationMainPeer);
                    else d = nextPeer.RegistrationId.GetDistanceTo(cryptoLibrary, destinationMainPeer.RegistrationId, numberOfDimensions);

                    if (minDistance == null || minDistance.IsGreaterThan(d))
                    {
                        bestNextPeer = nextPeer;
                        minDistance = d;
                        currentPeerIsBetterThanAnyNeighbor = false;
                        //    if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        //        $"@ RoutingIterationProcedure() peer={nextPeer} d={d}");
                    }
                }

                {
                    var d = currentPeer.RegistrationId.GetDistanceTo(cryptoLibrary, destinationMainPeer.RegistrationId, numberOfDimensions);
                    if (minDistance == null || minDistance.IsGreaterThan(d))
                    {
                        currentPeerIsBetterThanAnyNeighbor = true;
                    }

                    if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        $"@ RoutingIterationProcedure() currentPeer={currentPeer} d={d}");
                }
                if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"<< RoutingIterationProcedure() returns bestNextPeer={bestNextPeer} minDistance={minDistance} currentPeerIsBetterThanAnyNeighbor={currentPeerIsBetterThanAnyNeighbor}");
            }
            else
            { // consider P2pConnectionValueCalculator
                double? maxValue = null;

                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    // dont go back to source peer
                    if (avoidedPeers.Contains(nextPeer)) continue;

                    double value;
                    if (considerNeighborsOfNeighbors) value = nextPeer.GetMaxValueOfThisAndNeighbors(p2pcvc);
                    else value = p2pcvc.GetValue(nextPeer.RegistrationId);

                    if (maxValue == null || value > maxValue)
                    {
                        bestNextPeer = nextPeer;
                        maxValue = value;
                        currentPeerIsBetterThanAnyNeighbor = false;
                        //    if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        //        $"@ RoutingIterationProcedure() peer={nextPeer} d={d}");
                    }
                }

                {
                    var value = p2pcvc.GetValue(currentPeer.RegistrationId);
                    if (maxValue == null || value > maxValue)
                    {
                        currentPeerIsBetterThanAnyNeighbor = true;
                    }

                    if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                        $"@ RoutingIterationProcedure() currentPeer={currentPeer} value={value}");
                }
                if (EnableDetailedLogs) _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail,
                    $"<< RoutingIterationProcedure() returns bestNextPeer={bestNextPeer} maxValue={maxValue} currentPeerIsBetterThanAnyNeighbor={currentPeerIsBetterThanAnyNeighbor}");
            }


         
        }               
        public void Dispose()
        {
        }
    }
}
