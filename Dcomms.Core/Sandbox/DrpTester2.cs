using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace Dcomms.Sandbox
{
    public class DrpTester2 : IDisposable
    {
        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();
        const string DrpTesterVisionChannelModuleName = "drpTester2";
        
        DrpPeerEngine _ep;
        readonly List<DrpTesterPeerApp> _xList = new List<DrpTesterPeerApp>();
        IEnumerable<IVisiblePeer> VisiblePeers
        {
            get
            {
                foreach (var p in _ep.VisibleLocalPeers)
                    yield return p;
                foreach (var x in _xList)
                    foreach (var p in x.DrpPeerEngine.VisibleLocalPeers)
                        yield return p;
            }
        }
        public ICommand ShowPeers => new DelegateCommand(() =>
        {
            var list = VisiblePeers.ToList();
            _visionChannel.EmitListOfPeers("allPeers", DrpTesterVisionChannelModuleName, AttentionLevel.needsAttention, "all peers", list, VisiblePeersDisplayMode.allPeers);
        });

        void xList_BeginRegistrations(int index)
        {
            if (index >= _xList.Count)
            {
              //  xList_BeginExtendNeighbors();
                return;
            }
            var x = _xList[index];
            var sw = Stopwatch.StartNew();
            _visionChannel.Emit(x.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                AttentionLevel.guiActivity, $"registering...");
            x.DrpPeerEngine.BeginRegister(x.DrpPeerRegistrationConfiguration, x, (localDrpPeer) =>
            {
                x.LocalDrpPeer = localDrpPeer;
                _visionChannel.Emit(x.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                    AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
                x.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(5), () =>
                {
                    xList_BeginRegistrations(index + 1);

                });
            });         
        }
        //void xList_BeginExtendNeighbors()
        //{          
        //    int xIndex = 0;
        //    int neighborsCount = 2;
        //    Timer timer = null;
        //    timer = new Timer((o) =>
        //    {
        //        if (xIndex >= _xList.Count)
        //        {
        //            neighborsCount++;
        //            if (neighborsCount > 5)
        //            {
        //                timer.Dispose();
        //                return;
        //            }
        //            xIndex = 0;
        //        }                

        //        var app = _xList[xIndex];
        //        _visionChannel.Emit(app.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"extending neighbors: count={neighborsCount}...");
        //        app.DrpPeerRegistrationConfiguration.NumberOfNeighborsToKeep = neighborsCount;

        //        xIndex++;
        //    }, null, 0, 1000);
        //}
        
        readonly VisionChannel _visionChannel;
        public DrpTester2(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
            _ep = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                LocalPort = EpLocalPort,
                VisionChannel = visionChannel,
                VisionChannelSourceId = "EP",
                ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                SandboxModeOnly_DisablePoW = true
            });
            var epLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(_ep.CryptoLibrary);
           
            _ep.BeginCreateLocalPeer(epLocalDrpPeerConfig, new DrpTesterPeerApp(_ep, epLocalDrpPeerConfig), (rpLocalPeer) =>
            {
                for (int i = 0; i < 10; i++)
                {
                    var x = new DrpPeerEngine(new DrpPeerEngineConfiguration
                    {
                        InsecureRandomSeed = _insecureRandom.Next(),
                        VisionChannel = visionChannel,
                        ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                        VisionChannelSourceId = $"X{i}",
                        SandboxModeOnly_DisablePoW = true,
                        
                    });
                    var xLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(x.CryptoLibrary);
                    xLocalDrpPeerConfig.EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) };
                    xLocalDrpPeerConfig.NumberOfNeighborsToKeep = 5;
                    _xList.Add(new DrpTesterPeerApp(x, xLocalDrpPeerConfig));
                }
                xList_BeginRegistrations(0);
            });
        }
        
        public void Dispose()
        {
            _ep.Dispose();
            foreach (var x in _xList)
                x.DrpPeerEngine.Dispose();
        }
    }
}
