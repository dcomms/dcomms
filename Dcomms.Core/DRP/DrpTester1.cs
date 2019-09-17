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
        class User : IDrpRegisteredPeerApp
        {
            public void OnReceivedMessage(byte[] message)
            {
                throw new NotImplementedException();
            }

            public UserId OnReceivedInvite_LookupUser(RegistrationId remoteRegID)
            {
                throw new NotImplementedException();
            }

            public SessionDescription OnReceivedInvite_GetLocalSessionDescription(DMP.UserId requesterUserId)
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
            epConfig.LocalPeerRegistrationId = new RegistrationId(_ep.CryptoLibrary.GetPublicKeyEd25519(epConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
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
                aConfig.LocalPeerRegistrationId = new RegistrationId(_a.CryptoLibrary.GetPublicKeyEd25519(aConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
    
                
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
                xConfig.LocalPeerRegistrationId = new RegistrationId(_x.CryptoLibrary.GetPublicKeyEd25519(xConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
                var distance_eptoa = epConfig.LocalPeerRegistrationId.GetDistanceTo(_x.CryptoLibrary, aConfig.LocalPeerRegistrationId);
                var distance_xtoa = xConfig.LocalPeerRegistrationId.GetDistanceTo(_x.CryptoLibrary, aConfig.LocalPeerRegistrationId);
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
                nConfig.LocalPeerRegistrationId = new RegistrationId(_n.CryptoLibrary.GetPublicKeyEd25519(nConfig.LocalPeerRegistrationPrivateKey.ed25519privateKey));
                var distance_ntoa = nConfig.LocalPeerRegistrationId.GetDistanceTo(_n.CryptoLibrary, aConfig.LocalPeerRegistrationId);
                if (distance_ntoa.IsGreaterThan(distance_xtoa)) goto _retryn;


                var distance_xton = xConfig.LocalPeerRegistrationId.GetDistanceTo(_n.CryptoLibrary, nConfig.LocalPeerRegistrationId);
                var distance_epton = epConfig.LocalPeerRegistrationId.GetDistanceTo(_n.CryptoLibrary, nConfig.LocalPeerRegistrationId);
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
            UserRootPrivateKeys.CreateUserId(1, 1, _x.CryptoLibrary, out var xUserPrivateKeys, out var xUserID);

            var aUserCertificate = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_a.CryptoLibrary, aUserID, aUserPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            _aLocalDrpPeer.BeginSendInvite(aUserCertificate, _xLocalDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId, xUserID, 
                session =>
                {
                    //todo
                });
        }
    }
}
