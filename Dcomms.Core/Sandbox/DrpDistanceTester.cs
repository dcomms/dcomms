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
        public int NumberOfNeighbors { get; set; } = 10;
        public int NumberOfDimensions { get; set; } = 8;



        readonly VisionChannel _visionChannel;
        public DrpDistanceTester(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }


        class Peer
        {
            public RegistrationId RegistrationId;
            public readonly Dictionary<RegistrationId, Peer> Neighbors = new Dictionary<RegistrationId, Peer>();
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
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"creating peers... NumberOfPeers={numberOfPeers} NumberOfDimensions={NumberOfDimensions}");
                

                RegistrationIdDistance.NumberOfDimensions = NumberOfDimensions;


                var cryptoLibrary = CryptoLibraries.Library;

                var allPeers = new List<Peer>();
                for (int i = 0; i < numberOfPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(cryptoLibrary);
                    allPeers.Add(new Peer { RegistrationId = peerConfig.LocalPeerRegistrationId });
                }

                var numberOfNeighbors = NumberOfNeighbors;
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"connecting neighbors ... NumberOfPeers={numberOfPeers}, NumberOfNeighbors={numberOfNeighbors}");


                for (int i = 0; i < NumberOfEntryPeers; i++)
                {
                    var peer = allPeers[i];

                    for (int j = 0; j < i; j++)                      
                    {
                        var anotherPeer = allPeers[j];
                        peer.AddNeighbor(anotherPeer);
                    }
                }

                int numberOfThreads = 1;// 6;
                var waitHandles = new List<EventWaitHandle>();
                for (int i = 0; i < numberOfThreads; i++)
                    waitHandles.Add(new EventWaitHandle(false, EventResetMode.AutoReset));

                var connectA = new Action<int, EventWaitHandle>((threadId, wh) =>
                {
                    var rnd2 = new Random();
                    int n = 0;
                    for (int i = threadId; i < allPeers.Count; i += numberOfThreads, n++)
                    {
                        var peer = allPeers[i];
                       // if (n % 159 == 0)  _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                        ConnectToNeighbors(rnd2, cryptoLibrary, peer, allPeers, numberOfNeighbors);
                    }
                    wh.Set();
                });
                for (int i = 0; i < numberOfThreads; i++)
                    connectA.BeginInvoke(i, waitHandles[i], (ar) => connectA.EndInvoke(ar), null);
                
                WaitHandle.WaitAll(waitHandles.ToArray());
                foreach (var wh in waitHandles) wh.Dispose();

                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...connected neighbors");

                for (int i = 0; i < allPeers.Count; i++)
                {
                    var peer = allPeers[i];
                 //   _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"{i}: {peer.Neighbors.Count} neighbors");
                }


                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"testing number of hops");
                var rnd = new Random();
                var numbersOfHops = new List<int>();
                var successRates = new List<int>();
                for (int i = 0; i < 200; i++)
                {
                    var sourcePeer = allPeers[rnd.Next(allPeers.Count)];
                _retry:
                    var destinationPeer = allPeers[rnd.Next(allPeers.Count)];
                    if (destinationPeer == sourcePeer) goto _retry;

                    var routingSuccess = InviteRoutingProcedure(cryptoLibrary, sourcePeer, destinationPeer, out var numberOfHops);
                    if (routingSuccess) numbersOfHops.Add(numberOfHops);
                    successRates.Add(routingSuccess ? 100 : 0);
                }
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                    $"numberOfPeers={numberOfPeers}, numberOfNeighbors={numberOfNeighbors}, average number of hops = {(numbersOfHops.Count != 0 ? numbersOfHops.Average() : -1)}  count={numbersOfHops.Count}. success rate = {successRates.Average()}%");

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
        bool InviteRoutingProcedure(ICryptoLibrary cryptoLibrary, Peer fromPeer, Peer toPeer, out int numberOfHops)
        {
            numberOfHops = 0;

            int hopsRemaining = 30;
            var currentPeer = fromPeer;
            var previousPeer = currentPeer;
            while (currentPeer != toPeer && hopsRemaining > 0)
            {
                RegistrationIdDistance minDistanceToDestination = null;
                Peer bestNextPeer = null;
                foreach (var nextPeer in currentPeer.Neighbors.Values)
                {
                    // dont go back to source peer
                    if (nextPeer == previousPeer) continue;

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
                if (bestNextPeer == null) break; // dead end
                currentPeer = bestNextPeer;
            }
                       
            var success = currentPeer == toPeer;
            _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"success/failure: {success}. number of hops = {numberOfHops}");

            return success;
        }
                
        void RegisterRoutingProcedure(ICryptoLibrary cryptoLibrary, Peer peer, Peer entryPeerAlreadyConnected, out Peer closestNewNeighbor)
        {
            closestNewNeighbor = null;

            int hopsRemaining = 30;
            var currentPeer = entryPeerAlreadyConnected;
            var previousPeer = entryPeerAlreadyConnected;
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
                    closestNewNeighbor = bestNextPeer;
            }
        }


        public void Dispose()
        {
        }
    }
}
