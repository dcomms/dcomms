using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Dcomms.Sandbox
{
    class DrpTester2 : IDisposable
    {
        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();
        const string DrpTesterVisionChannelModuleName = "drpTester2";


        DrpPeerEngine _ep;
        readonly List<DrpTesterPeerApp> _xList = new List<DrpTesterPeerApp>();

        void xList_BeginRegistrations(int index)
        {
            if (index >= _xList.Count)
            {
                xList_BeginExtendNeighbors();
                return;
            }
            var x = _xList[index];
            index++;
            var sw = Stopwatch.StartNew();
            _visionChannel.Emit(x.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                AttentionLevel.guiActivity, $"registering...");
            x.DrpPeerEngine.BeginRegister(x.DrpPeerRegistrationConfiguration, x, (localDrpPeer) =>
            {
                x.LocalDrpPeer = localDrpPeer;
                _visionChannel.Emit(x.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                    AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
                xList_BeginRegistrations(index + 1);
            });
         
        }

        void xList_BeginExtendNeighbors()
        {

            //int index = 0;
            //Timer timer = null;
            //timer = new Timer((o) =>
            //{
            //    if (index >= _xList.Count)
            //    {
            //        timer.Dispose();

            //        return;
            //    }
            //     var x = _xList[index];
            //     index++;
            var app = _xList.First(x => x.LocalDrpPeer?.ConnectedNeighbors?.Count < 2);

            _visionChannel.Emit(app.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                AttentionLevel.guiActivity, $"extending neighbors...");
            app.DrpPeerRegistrationConfiguration.NumberOfNeighborsToKeep = 2;
            //}, null, 0, 1000);
        }


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
            });
            var epConfig = new DrpPeerRegistrationConfiguration
            {
              //  NumberOfNeighborsToKeep = 20,
            };
            epConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _ep.CryptoLibrary.GeneratePrivateKeyEd25519() };
            epConfig.LocalPeerRegistrationId = new RegistrationId(_ep.CryptoLibrary.GetPublicKeyEd25519(epConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
            _ep.BeginCreateLocalPeer(epConfig, new DrpTesterPeerApp(_ep, epConfig), (rpLocalPeer) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    var x = new DrpPeerEngine(new DrpPeerEngineConfiguration
                    {
                        InsecureRandomSeed = _insecureRandom.Next(),
                        VisionChannel = visionChannel,
                        ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                        VisionChannelSourceId = $"X{i}",
                        SandboxModeOnly_DisableRecentUniquePow1Data = true
                    });
                    var xConfig = new DrpPeerRegistrationConfiguration
                    {
                        EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) }
                    };
                    xConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = x.CryptoLibrary.GeneratePrivateKeyEd25519() };
                    xConfig.LocalPeerRegistrationId = new RegistrationId(x.CryptoLibrary.GetPublicKeyEd25519(xConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));

                    _xList.Add(new DrpTesterPeerApp(x, xConfig));
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
