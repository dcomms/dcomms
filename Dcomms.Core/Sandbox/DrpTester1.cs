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
        const int NumberOfDimensions = 8;

        const int EpLocalPort = 6789;
        readonly Random _insecureRandom = new Random();
        DrpPeerEngine _ep, _a;
        DrpPeerEngine _x, _n;
        LocalDrpPeer _xLocalDrpPeer, _aLocalDrpPeer, _nLocalDrpPeer;
        DrpTesterPeerApp _aUser, _xUser;
        readonly VisionChannel _visionChannel;
        const string DrpTesterVisionChannelModuleName = "drpTester1";
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
                SandboxModeOnly_NumberOfDimensions = NumberOfDimensions
            });
            var epLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(_ep.CryptoLibrary, NumberOfDimensions);
          
            _ep.BeginCreateLocalPeer(epLocalDrpPeerConfig, new DrpTesterPeerApp(_ep, epLocalDrpPeerConfig), (rpLocalPeer) =>
            {   
                _a = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    VisionChannel = visionChannel,
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    VisionChannelSourceId = "A",
                    SandboxModeOnly_NumberOfDimensions = NumberOfDimensions
                });
                var aLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(_a.CryptoLibrary, NumberOfDimensions);
                aLocalDrpPeerConfig.EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) };
                              
                _x = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    VisionChannel = visionChannel,
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    VisionChannelSourceId = "X",
                    SandboxModeOnly_NumberOfDimensions = NumberOfDimensions
                });

            _retryx:
                var xLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(_x.CryptoLibrary, NumberOfDimensions);
                var distance_eptoa = epLocalDrpPeerConfig.LocalPeerRegistrationId.GetDistanceTo(_x.CryptoLibrary, aLocalDrpPeerConfig.LocalPeerRegistrationId, NumberOfDimensions);
                var distance_xtoa = xLocalDrpPeerConfig.LocalPeerRegistrationId.GetDistanceTo(_x.CryptoLibrary, aLocalDrpPeerConfig.LocalPeerRegistrationId, NumberOfDimensions);
                if (distance_xtoa.IsGreaterThan(distance_eptoa)) goto _retryx;
                xLocalDrpPeerConfig.EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) };
                
                _n = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    VisionChannel = visionChannel,
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    VisionChannelSourceId = "N",
                    SandboxModeOnly_NumberOfDimensions = NumberOfDimensions
                });


            _retryn:
                var nLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(_n.CryptoLibrary, NumberOfDimensions);               
                var distance_ntoa = nLocalDrpPeerConfig.LocalPeerRegistrationId.GetDistanceTo(_n.CryptoLibrary, aLocalDrpPeerConfig.LocalPeerRegistrationId, NumberOfDimensions);
                if (distance_ntoa.IsGreaterThan(distance_xtoa)) goto _retryn;
                nLocalDrpPeerConfig.EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) };

                var distance_xton = xLocalDrpPeerConfig.LocalPeerRegistrationId.GetDistanceTo(_n.CryptoLibrary, nLocalDrpPeerConfig.LocalPeerRegistrationId, NumberOfDimensions);
                var distance_epton = epLocalDrpPeerConfig.LocalPeerRegistrationId.GetDistanceTo(_n.CryptoLibrary, nLocalDrpPeerConfig.LocalPeerRegistrationId, NumberOfDimensions);
                if (distance_xton.IsGreaterThan(distance_epton)) goto _retryn;

                _xUser = new DrpTesterPeerApp(_x, xLocalDrpPeerConfig);
                var swX = Stopwatch.StartNew();
                _x.BeginRegister(xLocalDrpPeerConfig, _xUser, (xLocalPeer) =>
                {
                    _visionChannel.Emit(_x.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        AttentionLevel.guiActivity, $"registration complete in {(int)swX.Elapsed.TotalMilliseconds}ms");
                  
                    _xLocalDrpPeer = xLocalPeer;
                    var swN = Stopwatch.StartNew();
                    _n.BeginRegister(nLocalDrpPeerConfig, new DrpTesterPeerApp(_n, nLocalDrpPeerConfig), (nLocalPeer) =>
                    {
                        _visionChannel.Emit(_n.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                            AttentionLevel.guiActivity, $"registration complete in {(int)swN.Elapsed.TotalMilliseconds}ms");
                        _nLocalDrpPeer = nLocalPeer;
                        _aUser = new DrpTesterPeerApp(_a, aLocalDrpPeerConfig);
                        var swA = Stopwatch.StartNew();
                        _a.BeginRegister(aLocalDrpPeerConfig, _aUser, (aLocalPeer) =>
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
            _xUser.ContactBookUsersByRegId.Add(_aLocalDrpPeer.Configuration.LocalPeerRegistrationId, _aUser.UserId);

            var aUserCertificate = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_a.CryptoLibrary, _aUser.UserId, _aUser.UserRootPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));


            var message = $"test Dcomms message {new Random().Next()}";
            _a.Configuration.VisionChannel.Emit(_a.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                $"sending message: {message}");

            _aLocalDrpPeer.BeginSendShortSingleMessage(aUserCertificate, _xLocalDrpPeer.Configuration.LocalPeerRegistrationId, _xUser.UserId, message,
                (exc) =>
                {
                    if (exc == null) _visionChannel.Emit(_a.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                        $"message was sent successfully");
                    else _visionChannel.Emit(_a.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.mediumPain,
                        $"message was not sent successfully");
                });
        }
    }
}
