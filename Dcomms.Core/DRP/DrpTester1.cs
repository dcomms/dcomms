using Dcomms.DMP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    /// <summary>
    /// sandbox for development since 2019-07
    /// </summary>
    class DrpTester1: IDisposable
    {
        class User : IDrpRegisteredPeerUser
        {
            public void OnReceivedMessage(byte[] message)
            {
                throw new NotImplementedException();
            }

            public UserID_PublicKeys OnReceivedInvite_LookupUser(RegistrationPublicKey remoteRegID)
            {
                throw new NotImplementedException();
            }

            public SessionDescription OnReceivedInvite_GetLocalSessionDescription(DMP.UserID_PublicKeys requesterUserId)
            {
                throw new NotImplementedException();
            }
            public void OnAcceptedIncomingInvite(Session session)
            {
                throw new NotImplementedException();
            }
        }

        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();
        DrpPeerEngine _ep, _a;
        DrpPeerEngine _x, _n;
        LocalDrpPeer _xLocalDrpPeer, _aLocalDrpPeer, _nLocalDrpPeer;
        public DrpTester1(VisionChannel visionChannel, Action cb = null)
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
    
                
                _x = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    VisionChannel = visionChannel,
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    VisionChannelSourceId = "X"
                });
                var xConfig = new DrpPeerRegistrationConfiguration
                {
                    EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) },
                    NumberOfNeighborsToKeep = 10
                };

            _retryx:
                xConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _x.CryptoLibrary.GeneratePrivateKeyEd25519() };
                xConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey(_x.CryptoLibrary.GetPublicKeyEd25519(xConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
                var distance_eptoa = epConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(_x.CryptoLibrary, aConfig.LocalPeerRegistrationPublicKey);
                var distance_xtoa = xConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(_x.CryptoLibrary, aConfig.LocalPeerRegistrationPublicKey);
                if (distance_xtoa.IsGreaterThan(distance_eptoa)) goto _retryx;


                _n = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    VisionChannel = visionChannel,
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    VisionChannelSourceId = "N"
                });
                var nConfig = new DrpPeerRegistrationConfiguration
                {
                    EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) },
                    NumberOfNeighborsToKeep = 10
                };

            _retryn:
                nConfig.LocalPeerRegistrationPrivateKey = new RegistrationPrivateKey { ed25519privateKey = _x.CryptoLibrary.GeneratePrivateKeyEd25519() };
                nConfig.LocalPeerRegistrationPublicKey = new RegistrationPublicKey(_n.CryptoLibrary.GetPublicKeyEd25519(nConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
                var distance_ntoa = nConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(_n.CryptoLibrary, aConfig.LocalPeerRegistrationPublicKey);
                if (distance_ntoa.IsGreaterThan(distance_xtoa)) goto _retryn;


                var distance_xton = xConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(_n.CryptoLibrary, nConfig.LocalPeerRegistrationPublicKey);
                var distance_epton = epConfig.LocalPeerRegistrationPublicKey.GetDistanceTo(_n.CryptoLibrary, nConfig.LocalPeerRegistrationPublicKey);
                if (distance_xton.IsGreaterThan(distance_epton)) goto _retryn;

                _x.BeginRegister(xConfig, new User(), (xLocalPeer) =>
                {
                    _xLocalDrpPeer = xLocalPeer;
                    _n.BeginRegister(nConfig, new User(), (nLocalPeer) =>
                    {
                        _nLocalDrpPeer = nLocalPeer;
                        _a.BeginRegister(aConfig, new User(), (aLocalPeer) =>
                        {
                            _aLocalDrpPeer = aLocalPeer;
                            if (cb != null) cb();
                        });
                    });
                });               
                

            });                    
        }
        public void Dispose()
        {
            _ep.Dispose();
            _a.Dispose();
            _x.Dispose();
            _n.Dispose();
        }

        public async Task SendInvite_AtoX_Async()
        {
            UserRootPrivateKeys.CreateUserId(1, 1, _a.CryptoLibrary, out var aUserPrivateKeys, out var aUserID);
           \
            var aUserCertificate = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_a.CryptoLibrary, aUserID, aUserPrivateKeys, );
            _aLocalDrpPeer.BeginSendInvite();
        }
    }
}
