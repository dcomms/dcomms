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
        public DrpTester1()
        {
            _rp = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                LocalPort = RpLocalPort
            });

            _a = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {


            });



            var aConfig = new DrpPeerRegistrationConfiguration
            {
                RendezvousPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, RpLocalPort) },
                NumberOfNeighborsToKeep = 10
            };
            aConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _a.CryptoLibrary.GeneratePrivateKeyEd25519() };
            aConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey { ed25519publicKey = _a.CryptoLibrary.GetPublicKeyEd25519(aConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey) };

            _a.RegisterAsync(aConfig, new User());
                       
        }
        public void Dispose()
        {
            _rp.Dispose();
        }
    }
}
