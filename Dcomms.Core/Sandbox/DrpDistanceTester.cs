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
        public int NumberOfPeers { get; set; } = 1000;
        public int NumberOfEntryPeers { get; set; } = 10;
        public int NumberOfNeighbors { get; set; } = 5;
        public int NumberOfDimensions { get; set; } = 8;
        public int ApoptosisIterationsCount { get; set; } = 5;


        readonly VisionChannel _visionChannel;
        public DrpDistanceTester(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }


        class Peer: IVisiblePeer
        {
            public Peer(ICryptoLibrary cryptoLibrary, RegistrationId registrationId)
            {
                RegistrationId = registrationId;
                VectorValues = RegistrationIdDistance.GetVectorValues(cryptoLibrary, registrationId);
            }
            public RegistrationId RegistrationId;
            public readonly Dictionary<RegistrationId, Peer> Neighbors = new Dictionary<RegistrationId, Peer>();

            public double[] VectorValues { get; private set; }
             

            IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => Neighbors.Values;

            public void AddNeighbor(Peer anotherPeer)
            {
                Neighbors.Add(anotherPeer.RegistrationId, anotherPeer);
                anotherPeer.Neighbors.Add(this.RegistrationId, this);
            }
        }

        public ICommand Test => new DelegateCommand(() =>
        {
            var a = new Action(() =>
            {
                var numberOfPeers = NumberOfPeers;
                var apoptosisIterationsCount = ApoptosisIterationsCount;
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"creating peers... NumberOfPeers={numberOfPeers} NumberOfDimensions={NumberOfDimensions}");
                

                RegistrationIdDistance.NumberOfDimensions = NumberOfDimensions;
                var numberOfNeighbors = NumberOfNeighbors;


                var cryptoLibrary = CryptoLibraries.Library;

                var allPeers = new List<Peer>();
                for (int i = 0; i < numberOfPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(cryptoLibrary);
                    allPeers.Add(new Peer(cryptoLibrary, peerConfig.LocalPeerRegistrationId));
                }

               
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"connecting neighbors... NumberOfPeers={numberOfPeers}, NumberOfNeighbors={numberOfNeighbors}");


                for (int i = 0; i < NumberOfEntryPeers; i++)
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

                for (int numberOfNeighborsI = 2; numberOfNeighborsI < numberOfNeighbors; numberOfNeighborsI++)
                {
                    for (int i = NumberOfEntryPeers; i < allPeers.Count; i++)
                    {
                        var peer = allPeers[i];
                        // if (n % 159 == 0)  _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                        ConnectToNeighbors(rnd2, cryptoLibrary, peer, allPeers, numberOfNeighborsI);
                    }
                }

                // apoptosis
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"connecting neighbors... apoptosis...");
                for (int i = 0; i < apoptosisIterationsCount; i++)
                {
                    for (int j = NumberOfEntryPeers; j < allPeers.Count; j++)
                    {
                        var peer = allPeers[j];
                        NeighborsApoptosisProcedure(cryptoLibrary, peer);
                        // if (n % 159 == 0)  _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                        ConnectToNeighbors(rnd2, cryptoLibrary, peer, allPeers, numberOfNeighbors);
                    }
                }

                //    wh.Set();
                //  });
                //   for (int i = 0; i < numberOfThreads; i++)
                //      connectA.BeginInvoke(i, waitHandles[i], (ar) => connectA.EndInvoke(ar), null);

                //    WaitHandle.WaitAll(waitHandles.ToArray());
                //    foreach (var wh in waitHandles) wh.Dispose();

                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...connected neighbors");

                for (int i = 0; i < allPeers.Count; i++)
                {
                    var peer = allPeers[i];
                 //   _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"{i}: {peer.Neighbors.Count} neighbors");
                }


                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"testing number of hops");
                var rnd = new Random();
                for (int maxNumberOfHops = 15; maxNumberOfHops < 200; maxNumberOfHops += 15)
                {

                    var numbersOfHops = new List<int>();
                    var successRates = new List<int>();
                    for (int i = 0; i < 200; i++)
                    {
                        var sourcePeer = allPeers[rnd.Next(allPeers.Count)];
                    _retry:
                        var destinationPeer = allPeers[rnd.Next(allPeers.Count)];
                        if (destinationPeer == sourcePeer) goto _retry;

                        var routingSuccess = InviteRoutingProcedure(cryptoLibrary, sourcePeer, destinationPeer, maxNumberOfHops, out var numberOfHops);
                        if (routingSuccess) numbersOfHops.Add(numberOfHops);
                        successRates.Add(routingSuccess ? 100 : 0);
                    }
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                        $"apoptosisIterationsCount={apoptosisIterationsCount},maxNumberOfHops ={maxNumberOfHops}, numberOfPeers={numberOfPeers}, numberOfNeighbors={numberOfNeighbors}, average number of hops = {(numbersOfHops.Count != 0 ? numbersOfHops.Average() : -1)}  count={numbersOfHops.Count}. success rate = {successRates.Average()}%");
                }

            });
            a.BeginInvoke((ar) => a.EndInvoke(ar), null);
        });
        void ConnectToNeighbors(Random rnd, ICryptoLibrary cryptoLibrary, Peer peer, List<Peer> allPeers, int numberOfNeighbors)
        {
            while (peer.Neighbors.Count < numberOfNeighbors)
            {
                if (!ConnectToNewNeighbor(cryptoLibrary, peer, allPeers, rnd)) break;
            }
        }
        void NeighborsApoptosisProcedure(ICryptoLibrary cryptoLibrary, Peer peer)
        {
            if (peer.Neighbors.Count > 1)
            {
                RegistrationIdDistance maxDistanceToNeighbor = null;
                Peer worstNeighbor = null;
                foreach (var neighborRegistrationId in peer.Neighbors.Keys)
                {
                    var d = neighborRegistrationId.GetDistanceTo(cryptoLibrary, peer.RegistrationId);
                    if (maxDistanceToNeighbor == null || d.IsGreaterThan(maxDistanceToNeighbor))
                    {
                        worstNeighbor = peer.Neighbors[neighborRegistrationId];
                        maxDistanceToNeighbor = d;
                    }
                }

                if (worstNeighbor != null)
                {
                    peer.Neighbors.Remove(worstNeighbor.RegistrationId);
                    worstNeighbor.Neighbors.Remove(peer.RegistrationId);
                }
            }
        }
        bool ConnectToNewNeighbor(ICryptoLibrary cryptoLibrary, Peer peer, List<Peer> allPeers, Random rnd) // = registration procedure in DRP
        {
            if (peer.Neighbors.Count == 0)
            {
                var entryPeer = allPeers[rnd.Next(NumberOfEntryPeers)];

                RegisterRoutingProcedure(cryptoLibrary, peer, entryPeer, out Peer newNeighbor);

                if (newNeighbor != null)
                {
                    peer.AddNeighbor(newNeighbor);
                    return true;
                }
            }
            else
            {
                var entryPeer = peer.Neighbors.Values.ToList()[rnd.Next(peer.Neighbors.Count)];

                RegisterRoutingProcedure(cryptoLibrary, peer, entryPeer, out var newNeighbor);
              
                if (newNeighbor != null)
                {
                    peer.AddNeighbor(newNeighbor);
                    return true;
                }
            }               
           
            return false;

            // TODO    near or far connection?? 50% 

        }

        /// <returns>true if reached toPeer via p2p conenctions</returns>
        bool InviteRoutingProcedure(ICryptoLibrary cryptoLibrary, Peer fromPeer, Peer toPeer, int maxNumberOfHops, out int numberOfHops)
        {
            numberOfHops = 0;

            var visiblePeers = new List<IVisiblePeer>();
            int hopsRemaining = maxNumberOfHops;
            var currentPeer = fromPeer;
            visiblePeers.Add(currentPeer);
            var previousPeer = currentPeer;
            _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"@InviteRoutingProcedure from {fromPeer.RegistrationId} to {toPeer.RegistrationId}");
            var proxyPeers = new HashSet<Peer>();
            while (currentPeer != toPeer)
            {
                RegistrationIdDistance minDistanceToDestination = null;
                Peer bestNextPeer = null;
                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    // dont go back to source peer
                    if (nextPeer == previousPeer) continue;

                    // dont go back to peer which is already used in the routing
                    if (proxyPeers.Contains(nextPeer)) continue;

                    var d = nextPeer.RegistrationId.GetDistanceTo(cryptoLibrary, toPeer.RegistrationId);
                    if (minDistanceToDestination == null || minDistanceToDestination.IsGreaterThan(d))
                    {
                        bestNextPeer = nextPeer;
                        minDistanceToDestination = d;
                    }
                }
                numberOfHops++;
                hopsRemaining--;
                previousPeer = currentPeer;
                if (bestNextPeer == null)
                {
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...dead end");
                    break; 
                }
                currentPeer = bestNextPeer;
                proxyPeers.Add(currentPeer);
                visiblePeers.Add(currentPeer);

                if (hopsRemaining <= 0)
                {
                    _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...number of hops reached zero");
                    break;
                }
            }
                       
            var success = currentPeer == toPeer;
            if (!success)
                visiblePeers.Add(toPeer);

            _visionChannel.EmitListOfPeers(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"success/failure: {success}. number of hops = {numberOfHops}", visiblePeers);

            return success;
        }
                
        void RegisterRoutingProcedure(ICryptoLibrary cryptoLibrary, Peer peer, Peer entryPeerAlreadyConnected, out Peer closestNewNeighbor)
        {
            closestNewNeighbor = null;

            int hopsRemaining = 30;
            var currentPeer = entryPeerAlreadyConnected;
            var previousPeer = entryPeerAlreadyConnected;
            var proxyPeers = new HashSet<Peer>();
            while (hopsRemaining > 0)
            {
                RegistrationIdDistance minDistanceToDestination = null;
                Peer bestNextPeer = null;
                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    // dont go back to previous hop
                    if (nextPeer == previousPeer) continue;
                    // dont go connect to same peer
                    if (nextPeer == peer) continue;
                    // dont connect if they are already neighbors
                    if (nextPeer.Neighbors.ContainsKey(peer.RegistrationId)) continue;
                    // dont go via same peers
                    if (proxyPeers.Contains(peer)) continue;


                    var d = nextPeer.RegistrationId.GetDistanceTo(cryptoLibrary, peer.RegistrationId);
                    if (minDistanceToDestination == null || minDistanceToDestination.IsGreaterThan(d))
                    {
                        bestNextPeer = nextPeer;
                        minDistanceToDestination = d;
                    }
                }
                hopsRemaining--;
                previousPeer = currentPeer;
                if (bestNextPeer == null) break;
                if (bestNextPeer != entryPeerAlreadyConnected)
                {
                    closestNewNeighbor = bestNextPeer;

                    proxyPeers.Add(closestNewNeighbor);
                }
            }
        }
        
        public void Dispose()
        {
        }
    }
}
