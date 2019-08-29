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

        const int RpLocalPort = 6789;
        DrpPeerEngine _rp, _a;
        List<DrpPeerEngine> _x_list;
        public DrpTester1(VisionChannel visionChannel)
        {
            _rp = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                LocalPort = RpLocalPort,
                VisionChannel = visionChannel,
                VisionChannelSourceId = "EP"
            });
            var rpConfig = new DrpPeerRegistrationConfiguration
            {
                NumberOfNeighborsToKeep = 20
            };
            rpConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _rp.CryptoLibrary.GeneratePrivateKeyEd25519() };
            rpConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey { ed25519publicKey = _rp.CryptoLibrary.GetPublicKeyEd25519(rpConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey) };
            _rp.CreateLocalPeer(rpConfig, new User());


            _x_list = new List<DrpPeerEngine>();
            for (int i = 0; i < 1; i++)
            {
                var x = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    VisionChannel = visionChannel,
                    LocalForcedPublicIpForRegistration = IPAddress.Loopback,
                    VisionChannelSourceId = $"X{i}"
                });
                var xConfig = new DrpPeerRegistrationConfiguration
                {
                    EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, RpLocalPort) },
                    NumberOfNeighborsToKeep = 10
                };
                xConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = x.CryptoLibrary.GeneratePrivateKeyEd25519() };
                xConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey { ed25519publicKey = x.CryptoLibrary.GetPublicKeyEd25519(xConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey) };

                x.BeginRegister(xConfig, new User());
                _x_list.Add(x);
            }

            _a = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                VisionChannel = visionChannel,
                LocalForcedPublicIpForRegistration = IPAddress.Loopback,
                VisionChannelSourceId = "A"
            });   
            var aConfig = new DrpPeerRegistrationConfiguration
            {
                EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, RpLocalPort) },
                NumberOfNeighborsToKeep = 10
            };
            aConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _a.CryptoLibrary.GeneratePrivateKeyEd25519() };
            aConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey { ed25519publicKey = _a.CryptoLibrary.GetPublicKeyEd25519(aConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey) };

            _a.BeginRegister(aConfig, new User());                       
        }
        public void Dispose()
        {
            _rp.Dispose();
        }
    }
}
