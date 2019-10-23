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
        
        readonly List<DrpTesterPeerApp> _xList = new List<DrpTesterPeerApp>();
        readonly List<DrpTesterPeerApp> _epList = new List<DrpTesterPeerApp>();
        IEnumerable<IVisiblePeer> VisiblePeers
        {
            get
            {
                foreach (var ep in _epList)
                    foreach (var p in ep.DrpPeerEngine.VisibleLocalPeers)
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

        //void xList_BeginRegistrations(int index)
        //{
        //    if (index >= _xList.Count)
        //    {
        //        xList_BeginExtendNeighbors();
        //        return;
        //    }
        //    var x = _xList[index];
        //    var sw = Stopwatch.StartNew();
        //    _visionChannel.Emit(x.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
        //        AttentionLevel.guiActivity, $"registering...");
        //    ShowPeers.Execute(null);
        //    x.DrpPeerEngine.BeginRegister(x.DrpPeerRegistrationConfiguration, x, (localDrpPeer) =>
        //    {
        //        x.LocalDrpPeer = localDrpPeer;
        //        _visionChannel.Emit(x.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
        //            AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
        //        x.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
        //        {
        //            xList_BeginRegistrations(index + 1);
        //        });
        //    });         
        //}
        //void xList_BeginExtendNeighbors()
        //{
        //    int xIndex = 0;
        //    int neighborsCount = 3;
        //    Timer timer = null;
        //    timer = new Timer((o) =>
        //    {
        //        if (xIndex >= _xList.Count)
        //        {
        //            neighborsCount++;
        //            if (neighborsCount > 13)
        //            {
        //                timer.Dispose();
        //                return;
        //            }
        //            xIndex = 0;
        //        }

        //        var app = _xList[xIndex];
        //        _visionChannel.Emit(app.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"extending neighbors: count={neighborsCount}...");
        //        ShowPeers.Execute(null);
        //        app.DrpPeerRegistrationConfiguration.MinDesiredNumberOfNeighbors = neighborsCount;

        //        xIndex++;
        //    }, null, 0, 500);
        //}
        void xList_BeginCreate(int index)
        {

            var x = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                VisionChannel = _visionChannel,
                ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                VisionChannelSourceId = $"X{index}",
                SandboxModeOnly_DisablePoW = true,

            });
            _visionChannel.Emit(x.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                AttentionLevel.guiActivity, $"creating peer index {index}...");
            ShowPeers.Execute(null);
            var xLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(x.CryptoLibrary);
            xLocalDrpPeerConfig.EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) };
            //  xLocalDrpPeerConfig.MinDesiredNumberOfNeighbors = 3;
            _xList.Add(new DrpTesterPeerApp(x, xLocalDrpPeerConfig));

            //xList_BeginRegistrations(0);

            if (index < 100)
            {
                x.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(10), () =>
                {
                    xList_BeginCreate(index + 1);
                });
            }
        }

        void BeginConnectEP(DrpTesterPeerApp ep, int epIndex, Action cb)
        {
            var endpoints = new List<IPEndPoint>();
            for (int i = epIndex+1; i < _epList.Count; i++)
            {
                endpoints.Add(
                        new IPEndPoint(IPAddress.Loopback, _epList[i].DrpPeerEngine.Configuration.LocalPort.Value)
                        );
            }

            _visionChannel.Emit(ep.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                AttentionLevel.guiActivity, $"connecting {ep} to other EPs...");
            ShowPeers.Execute(null);

            ep.LocalDrpPeer.BeginConnectToEPs(endpoints.ToArray(), cb);
        }

        void epList_BeginConnect(int index)
        {
            var ep = _epList[index];
            
            _visionChannel.Emit(ep.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                AttentionLevel.guiActivity, $"connecting to other EPs...");
            BeginConnectEP(ep, index, () =>
            {
                index++;
                if (index < _epList.Count-1)
                    epList_BeginConnect(index);
                else
                    xList_BeginCreate(0);
            });
        }        
        void epList_BeginCreateLocalPeer(int index)
        {
            var ep = _epList[index];

            ep.DrpPeerEngine.BeginCreateLocalPeer(ep.DrpPeerRegistrationConfiguration, ep, (epLocalPeer) =>
            {
                ep.LocalDrpPeer = epLocalPeer;
                index++;
                if (index < _epList.Count)
                    epList_BeginCreateLocalPeer(index);
                else
                    epList_BeginConnect(0);
            });
        }

        readonly VisionChannel _visionChannel;
        public DrpTester2(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
            for (int i = 0; i < 15; i++)
            {
                var ep = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    LocalPort = (ushort)(EpLocalPort+i),
                    VisionChannel = visionChannel,
                    VisionChannelSourceId = $"EP{i}",
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    SandboxModeOnly_DisablePoW = true
                });
                var epLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(ep.CryptoLibrary);
                _epList.Add(new DrpTesterPeerApp(ep, epLocalDrpPeerConfig));
            }
            epList_BeginCreateLocalPeer(0);
        }
        
        public void Dispose()
        {
            foreach (var ep in _epList)
                ep.DrpPeerEngine.Dispose();
            foreach (var x in _xList)
                x.DrpPeerEngine.Dispose();
        }
    }
}
