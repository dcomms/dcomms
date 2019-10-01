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
        public int? NumberOfPeers { get; set; } = 5000;
        public int? NumberOfNeighbors { get; set; } = 10;
        

        readonly VisionChannel _visionChannel;
        public DrpDistanceTester(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }


        class Peer
        {
            public RegistrationId RegistrationId;
            public readonly Dictionary<RegistrationId, Peer> Neighbors = new Dictionary<RegistrationId, Peer>();
        }

        public ICommand Test => new DelegateCommand(() =>
        {
            var a = new Action(() =>
            {
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"creating peers... NumberOfPeers={NumberOfPeers}");

                var cryptoLibrary = Cryptography.CryptoLibraries.Library;

                var allPeers = new List<Peer>();
                for (int i = 0; i < NumberOfPeers; i++)
                {
                    var peerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(cryptoLibrary);
                    allPeers.Add(new Peer { RegistrationId = peerConfig.LocalPeerRegistrationId });
                }
                                             

                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, $"connecting neighbors ... NumberOfPeers={NumberOfPeers}, NumberOfNeighbors={NumberOfNeighbors}");

                int numberOfThreads = 6;
                var waitHandles = new List<EventWaitHandle>();
                for (int i = 0; i < numberOfThreads; i++)
                    waitHandles.Add(new EventWaitHandle(false, EventResetMode.AutoReset));

                var connectA = new Action<int, EventWaitHandle>((threadId, wh) =>
                {
                    int n = 0;
                    for (int i = threadId; i < allPeers.Count; i += numberOfThreads, n++)
                    {
                        var peer = allPeers[i];
                        if (n % 159 == 0)
                            _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"connecting neighbors {i}/{allPeers.Count} ...");
                        ConnectToNeighbors(cryptoLibrary, peer, allPeers);
                    }
                    wh.Set();
                });
                for (int i = 0; i < numberOfThreads; i++)
                    connectA.BeginInvoke(i, waitHandles[i], (ar) => connectA.EndInvoke(ar), null);
                
                WaitHandle.WaitAll(waitHandles.ToArray());
                foreach (var wh in waitHandles) wh.Dispose();

                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"...connected neighbors");


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

                    var numberOfHops = TestRouting(cryptoLibrary, sourcePeer, destinationPeer);
                    if (numberOfHops.HasValue)
                        numbersOfHops.Add(numberOfHops.Value);
                    successRates.Add(numberOfHops.HasValue ? 100 : 0);

                }
                _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail,
                    $"average number of hops = {numbersOfHops.Average()}  count={numbersOfHops.Count}. success rate = {successRates.Average()}%");

            });
            a.BeginInvoke((ar) => a.EndInvoke(ar), null);
        });
        void ConnectToNeighbors(ICryptoLibrary cryptoLibrary, Peer peer, List<Peer> allPeers)
        {
            while (peer.Neighbors.Count < NumberOfNeighbors)
            {
                RegistrationIdDistance minDistance = null;
                Peer bestNeighbor = null;
                foreach (var potentialNeighbor in allPeers)
                {
                    if (!peer.Neighbors.ContainsKey(potentialNeighbor.RegistrationId) && !potentialNeighbor.RegistrationId.Equals(peer.RegistrationId))
                    {
                        var potentialNeighborDistance = potentialNeighbor.RegistrationId.GetDistanceTo(cryptoLibrary, peer.RegistrationId);
                        if (minDistance == null || minDistance.IsGreaterThan(potentialNeighborDistance))
                        {
                            minDistance = potentialNeighborDistance;
                            bestNeighbor = potentialNeighbor;
                        }
                    }
                }
                if (bestNeighbor != null) peer.Neighbors.Add(bestNeighbor.RegistrationId, bestNeighbor);
                else
                    break;
            }
        }

        int? TestRouting(ICryptoLibrary cryptoLibrary, Peer fromPeer, Peer toPeer)
        {
            int numberOfHops = 0;

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
                currentPeer = bestNextPeer;
            }
                       
            var success = currentPeer == toPeer;
            _visionChannel.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, $"success/failure: {success}. number of hops = {numberOfHops}");

            return success ? (int?)numberOfHops : null;
        }

        public void Dispose()
        {
        }
    }
}
