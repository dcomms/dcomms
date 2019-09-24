using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.Sandbox
{
    class DrpTester2 : IDisposable
    {
        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();


        DrpPeerEngine _ep;
        readonly List<DrpPeerEngine> _xList = new List<DrpPeerEngine>();
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
                NumberOfNeighborsToKeep = 20,
            };
            epConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _ep.CryptoLibrary.GeneratePrivateKeyEd25519() };
            epConfig.LocalPeerRegistrationId = new RegistrationId(_ep.CryptoLibrary.GetPublicKeyEd25519(epConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
            _ep.BeginCreateLocalPeer(epConfig, new DrpTesterPeerApp(_ep), (rpLocalPeer) =>
            {
                for (int i = 0; i < 100; i++)
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
                        EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) },
                        NumberOfNeighborsToKeep = 10
                    };
                    xConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = x.CryptoLibrary.GeneratePrivateKeyEd25519() };
                    xConfig.LocalPeerRegistrationId = new RegistrationId(x.CryptoLibrary.GetPublicKeyEd25519(xConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));

                    var xUser = new DrpTesterPeerApp(x);
                    x.BeginRegister(xConfig, xUser, (xLocalPeer) =>
                    {
                    });
                    _xList.Add(x);
                }
            });
        }


        public void Dispose()
        {
            _ep.Dispose();
            foreach (var x in _xList)
                x.Dispose();
        }
    }
}
