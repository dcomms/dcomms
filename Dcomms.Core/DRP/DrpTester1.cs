using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    class DrpTester1: IDisposable
    {
        class User : IDrpRegisteredPeerUser
        {
            public void OnReceivedMessage(byte[] message)
            {
                throw new NotImplementedException();
            }
        }

        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();
        DrpPeerEngine _ep, _a;
        List<DrpPeerEngine> _x_list;
        public DrpTester1(VisionChannel visionChannel)
        {
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
            epConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey(_ep.CryptoLibrary.GetPublicKeyEd25519(epConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
            _ep.BeginCreateLocalPeer(epConfig, new User(), (rpLocalPeer) =>
            {   
                _a = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    VisionChannel = visionChannel,
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    VisionChannelSourceId = "A"
                });
                var aConfig = new DrpPeerRegistrationConfiguration
                {
                    EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) },
                    NumberOfNeighborsToKeep = 10
                };
                aConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _a.CryptoLibrary.GeneratePrivateKeyEd25519() };
                aConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey(_a.CryptoLibrary.GetPublicKeyEd25519(aConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
    
                _x_list = new List<DrpPeerEngine>();
                for (int i = 0; i < 1; i++)
                {
                    var x = new DrpPeerEngine(new DrpPeerEngineConfiguration
                    {
                        InsecureRandomSeed = _insecureRandom.Next(),
                        VisionChannel = visionChannel,
                        ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                        VisionChannelSourceId = $"X{i}"
                    });
                    var xConfig = new DrpPeerRegistrationConfiguration
                    {
                        EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) },
                        NumberOfNeighborsToKeep = 10
                    };

              _retry:
                    xConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = x.CryptoLibrary.GeneratePrivateKeyEd25519() };
                    xConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey(x.CryptoLibrary.GetPublicKeyEd25519(xConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));

                    var distance_eptoa = epConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(x.CryptoLibrary, aConfig.LocalPeerRegistrationPublicKey);
                    var distance_xtoa = xConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(x.CryptoLibrary, aConfig.LocalPeerRegistrationPublicKey);
                    if (distance_xtoa.IsGreaterThan(distance_eptoa)) goto _retry;

                    x.BeginRegister(xConfig, new User());
                    _x_list.Add(x);
                }

                _a.BeginRegister(aConfig, new User());
            });                    
        }
        public void Dispose()
        {
            _ep.Dispose();
        }
    }
}
