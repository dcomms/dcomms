using Dcomms.Cryptography;
using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.Sandbox
{
    /// <summary>
    /// sandbox for development since 2019-07
    /// </summary>
    class DrpTester1: IDisposable
    {
        class User : IDrpRegisteredPeerApp
        {
            public readonly UserRootPrivateKeys UserRootPrivateKeys;
            public readonly UserId UserId;

            public readonly UserCertificate UserCertificateWithPrivateKey;
            public readonly DrpPeerEngine DrpPeerEngine;
            public User(DrpPeerEngine drpPeerEngine)
            {
                DrpPeerEngine = drpPeerEngine;
                UserRootPrivateKeys.CreateUserId(3, 2, DrpPeerEngine.CryptoLibrary, out UserRootPrivateKeys, out UserId);
                UserCertificateWithPrivateKey = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(DrpPeerEngine.CryptoLibrary, UserId, UserRootPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            }

            public void OnReceivedShortSingleMessage(string message)
            {
                DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                    $"received message: {message}");
            }
            public readonly Dictionary<RegistrationId, UserId> ContactBookUsersByRegId = new Dictionary<RegistrationId, UserId>();
            public void OnReceivedInvite(RegistrationId remoteRegistrationId, out DMP.UserId remoteUserId, out DMP.UserCertificate localUserCertificateWithPrivateKey, out bool autoReceiveShortSingleMessage)
            {
                remoteUserId = ContactBookUsersByRegId[remoteRegistrationId];
                localUserCertificateWithPrivateKey = UserCertificateWithPrivateKey;
                autoReceiveShortSingleMessage = true;
            }

            //public InviteSessionDescription OnReceivedInvite_GetLocalSessionDescription(DMP.UserId requesterUserId, out UserCertificate userCertificateWithPrivateKey)
            //{
            //    userCertificateWithPrivateKey = UserCertificateWithPrivateKey;
            //    var r = new InviteSessionDescription
            //    {
            //        SessionType = SessionType.asyncUserMessages,
            //        DirectChannelEndPoint = new IPEndPoint(IPAddress.Parse("1.2.3.4"), 56789),
            //        DirectChannelToken32 = new DirectChannelToken32 {  Token32=0x123456 }
            //    };

            //    DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
            //        $"responding with local session {r}");

            //    return r;
            //}
            //public void OnAcceptedIncomingInvite(InviteSession session)
            //{
            //    DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
            //        $"accepted remote session: {session.RemoteSessionDescription}");
            //}
        }

        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();
        DrpPeerEngine _ep, _a;
        DrpPeerEngine _x, _n;
        LocalDrpPeer _xLocalDrpPeer, _aLocalDrpPeer, _nLocalDrpPeer;
        User _aUser, _xUser;
        readonly VisionChannel _visionChannel;
        const string DrpTesterVisionChannelModuleName = "drpTester";
        public DrpTester1(VisionChannel visionChannel, Action cb = null)
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
            _ep.BeginCreateLocalPeer(epConfig, new User(_ep), (rpLocalPeer) =>
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

                _xUser = new User(_x);
                var swX = Stopwatch.StartNew();
                _x.BeginRegister(xConfig, _xUser, (xLocalPeer) =>
                {
                    _visionChannel.Emit(_x.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        AttentionLevel.guiActivity, $"registration complete in {(int)swX.Elapsed.TotalMilliseconds}ms");
                  
                    _xLocalDrpPeer = xLocalPeer;
                    var swN = Stopwatch.StartNew();
                    _n.BeginRegister(nConfig, new User(_n), (nLocalPeer) =>
                    {
                        _visionChannel.Emit(_n.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                            AttentionLevel.guiActivity, $"registration complete in {(int)swN.Elapsed.TotalMilliseconds}ms");
                        _nLocalDrpPeer = nLocalPeer;
                        _aUser = new User(_a);
                        var swA = Stopwatch.StartNew();
                        _a.BeginRegister(aConfig, _aUser, (aLocalPeer) =>
                        {
                            _visionChannel.Emit(_a.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                                AttentionLevel.guiActivity, $"registration complete in {(int)swA.Elapsed.TotalMilliseconds}ms");
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
            _xUser.ContactBookUsersByRegId.Add(_aLocalDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId, _aUser.UserId);

            var aUserCertificate = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_a.CryptoLibrary, _aUser.UserId, _aUser.UserRootPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));


            var message = $"test Dcomms message {new Random().Next()}";
            _a.Configuration.VisionChannel.Emit(_a.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                $"sending message: {message}");

            _aLocalDrpPeer.BeginSendShortSingleMessage(aUserCertificate, _xLocalDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId, _xUser.UserId, message,
                () =>
                {
                    _visionChannel.Emit(_a.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                        $"message was sent successfully");
                });
        }
    }
}
