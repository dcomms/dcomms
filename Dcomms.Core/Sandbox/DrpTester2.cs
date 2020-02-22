using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Dcomms.Sandbox
{
    public class DrpTester2 : IDisposable
    {
        //const int NumberOfPeers = 300;
        //const int NumberOfDimensions = 2;
        //const int MinDesiredNumberOfNeighbors = 6;
        //const int SoftMaxDesiredNumberOfNeighbors = 8;
        //const int AbsoluteMaxDesiredNumberOfNeighbors = 12;
        //const int MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = 60;
        //const double NeighborhoodExtensionMinIntervalS = 0.5;
        //const int NumberOfEPs = 10;
        //const int EpAbsoluteMaxDesiredNumberOfNeighbors = 13;
        //const int EpSoftMaxDesiredNumberOfNeighbors = 11;
        //const int EpMinDesiredNumberOfNeighbors = 8;


        const int NumberOfPeers = 100;
        const int NumberOfDimensions = 8;
        const int MinDesiredNumberOfNeighbors = 12;
        const int SoftMaxDesiredNumberOfNeighbors = 14;
        const int AbsoluteMaxDesiredNumberOfNeighbors = 18;
        const int MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = 60;
        const double NeighborhoodExtensionMinIntervalS = 0.2;
        const int NumberOfEPs = 25;
        const int EpAbsoluteMaxDesiredNumberOfNeighbors = 40;
        const int EpSoftMaxDesiredNumberOfNeighbors = 30;
        const int EpMinDesiredNumberOfNeighbors = 13;
        const int NumberOfPeersToStartMessagesTest = 20;


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
            EmitAllPeers(AttentionLevel.needsAttention, $"all peers on GUI request {DateTime.Now}");
        });
        void EmitAllPeers(AttentionLevel level, string message)
        {
            _visionChannel.EmitListOfPeers("allPeers", DrpTesterVisionChannelModuleName, level, message + $" ({_xList.Count} peers)");

        }

        void xList_BeginCreate(int index, int? copyRegIdFromIndexNullable)
        {
            string visionChannelSourceId = $"X{index}";
            if (copyRegIdFromIndexNullable != null) visionChannelSourceId += $"_copyFrom{copyRegIdFromIndexNullable}";

            var x = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                VisionChannel = _visionChannel,
                ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                VisionChannelSourceId = visionChannelSourceId,
                SandboxModeOnly_DisablePoW = true,
                SandboxModeOnly_EnableInsecureLogs = true,
                SandboxModeOnly_NumberOfDimensions = NumberOfDimensions,
                NeighborhoodExtensionMinIntervalS = NeighborhoodExtensionMinIntervalS
            });

            EmitAllPeers(AttentionLevel.guiActivity, $"creating peer index {index} (copyRegIdFromIndex={copyRegIdFromIndexNullable})...");

            byte[] ed25519privateKey = null; RegistrationId registrationId = null;
            if (copyRegIdFromIndexNullable.HasValue)
            {
                var copyFromUser = _xList[copyRegIdFromIndexNullable.Value];
                ed25519privateKey = copyFromUser.DrpPeerRegistrationConfiguration.LocalPeerRegistrationPrivateKey.ed25519privateKey;
                registrationId = copyFromUser.DrpPeerRegistrationConfiguration.LocalPeerRegistrationId;
            }
            var xLocalDrpPeerConfig = LocalDrpPeerConfiguration.Create(x.CryptoLibrary, NumberOfDimensions, ed25519privateKey, registrationId);
            xLocalDrpPeerConfig.EntryPeerEndpoints = new[] { new IPEndPoint(IPAddress.Loopback, EpLocalPort) };
            xLocalDrpPeerConfig.MinDesiredNumberOfNeighbors = copyRegIdFromIndexNullable.HasValue ? 1 : MinDesiredNumberOfNeighbors;
            xLocalDrpPeerConfig.SoftMaxNumberOfNeighbors = copyRegIdFromIndexNullable.HasValue ? 1 : SoftMaxDesiredNumberOfNeighbors;
            xLocalDrpPeerConfig.AbsoluteMaxNumberOfNeighbors = copyRegIdFromIndexNullable.HasValue ? 1 : AbsoluteMaxDesiredNumberOfNeighbors;
            xLocalDrpPeerConfig.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS;
            var xDrpTesterPeerApp = new DrpTesterPeerApp(x, xLocalDrpPeerConfig);
            _xList.Add(xDrpTesterPeerApp);
            x.BeginRegister(xLocalDrpPeerConfig, xDrpTesterPeerApp, (localDrpPeer) =>
            {
                xDrpTesterPeerApp.LocalDrpPeer = localDrpPeer;
                _visionChannel.Emit(x.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                    AttentionLevel.guiActivity, $"registration with EP is complete. waiting for connection with neighbors...");
                x.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    xList_AfterEpRegistration_ContinueIfConnectedToNeighbors(xDrpTesterPeerApp, index);
                }, "waiting for connection with neighbors 234580");
            });
        }
        void xList_AfterEpRegistration_ContinueIfConnectedToNeighbors(DrpTesterPeerApp x, int index)
        {
            if (x.LocalDrpPeer.ConnectedNeighbors.Count >= x.LocalDrpPeer.Configuration.MinDesiredNumberOfNeighbors)
            {
                EmitAllPeers(AttentionLevel.guiActivity, $"{x} is connected with {x.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, enough to continue with other peers");
                if (index < NumberOfPeers)
                    xList_BeginCreate(index + 1, index == 0 ? (int?)0 : null);
                if (index > NumberOfPeersToStartMessagesTest) BeginTestInvitesIfNotStartedAlready();
            }
            else
            {
                EmitAllPeers(AttentionLevel.guiActivity, $"{x} is connected with {x.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, not enough to continue with other peers");
                x.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    xList_AfterEpRegistration_ContinueIfConnectedToNeighbors(x, index);
                }, "xList_AfterEpRegistration_ContinueIfConnectedToNeighbors2345");
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

            EmitAllPeers(AttentionLevel.guiActivity, $"connecting {ep} to other EPs...");
      

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
                if (index < _epList.Count - 1)
                    epList_BeginConnect(index);
                else
                {
                    EmitAllPeers(AttentionLevel.guiActivity, "connected all EPs");

                    foreach (var ep2 in _epList)
                    {
                        ep2.DrpPeerRegistrationConfiguration.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = null;
                        ep2.DrpPeerRegistrationConfiguration.AbsoluteMaxNumberOfNeighbors = EpAbsoluteMaxDesiredNumberOfNeighbors;
                        ep2.DrpPeerRegistrationConfiguration.SoftMaxNumberOfNeighbors = EpSoftMaxDesiredNumberOfNeighbors;
                        ep2.DrpPeerRegistrationConfiguration.MinDesiredNumberOfNeighbors = EpMinDesiredNumberOfNeighbors;
                    }

                    xList_BeginCreate(0, null);
                }
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
            _visionChannel.VisiblePeersDelegate = () => { return VisiblePeers.ToList(); };
            for (int i = 0; i < NumberOfEPs; i++)
            {
                var ep = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    LocalPort = (ushort)(EpLocalPort + i),
                    VisionChannel = visionChannel,
                    VisionChannelSourceId = $"EP{i}",
                    ForcedPublicIpApiProviderResponse = IPAddress.Loopback,
                    SandboxModeOnly_DisablePoW = true,
                    SandboxModeOnly_EnableInsecureLogs = true,
                    SandboxModeOnly_NumberOfDimensions = NumberOfDimensions
                }); ;
                var epLocalDrpPeerConfig = LocalDrpPeerConfiguration.Create(ep.CryptoLibrary, NumberOfDimensions);
                epLocalDrpPeerConfig.MinDesiredNumberOfNeighbors = null;
                epLocalDrpPeerConfig.AbsoluteMaxNumberOfNeighbors = null;
                epLocalDrpPeerConfig.SoftMaxNumberOfNeighbors = null;
                epLocalDrpPeerConfig.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = null;
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

        Random _rnd = new Random();
        int _inviteTestsCounter = 0;
        void BeginTestInvites(InvitesTest test, Action cb = null)
        {           
            var peer1 = test.Peers[_inviteTestsCounter++ % test.Peers.Count];

        _retry:
            var peer2 = test.Peers[_rnd.Next(test.Peers.Count)];
            if (peer1 == peer2) goto _retry;

            EmitAllPeers(AttentionLevel.guiActivity, $"testing message from {peer1} to {peer2} ({test.counter}/{test.MaxCount}) {DateTime.Now}");
               
            var userCertificate1 = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(peer1.DrpPeerEngine.CryptoLibrary, peer1.UserId, peer1.UserRootPrivateKeys, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

            var text = $"test{_rnd.Next()}-{_rnd.Next()}_from_{peer1}_to_{peer2}";
            peer1.LocalDrpPeer.BeginSendShortSingleMessage(userCertificate1, peer2.LocalDrpPeer.Configuration.LocalPeerRegistrationId, peer2.UserId, text, null, (exc,ep) =>
            {
                test.counter++;
                
                if (peer2.LatestReceivedTextMessage == text)
                {
                    test.successfulCount++;
                    _visionChannel.Emit(peer1.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        AttentionLevel.guiActivity, $"successfully tested message from {peer1} to {peer2}. success rate = {(double)test.successfulCount * 100 / test.counter}% ({test.successfulCount}/{test.counter})");
                }
                else
                {
                    EmitAllPeers(AttentionLevel.mediumPain, $"test message failed from {peer1} to {peer2}: received '{peer2.LatestReceivedTextMessage}', expected '{text}");
                }

                if (test.counter < test.MaxCount) BeginTestInvites(test, cb);
                else
                {
                    var successRatePercents = (double)test.successfulCount * 100 / test.counter;
                    var level = successRatePercents == 100 ? AttentionLevel.guiActivity : (successRatePercents > 99 ? AttentionLevel.lightPain : AttentionLevel.mediumPain);
                    _visionChannel.Emit(peer1.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        level, $"messages test is complete: success rate = {successRatePercents}%");
                    cb?.Invoke();
                }
            });
        }
        class InvitesTest
        {
            public int counter;
            public int MaxCount = 100;
            public int successfulCount;
            public List<DrpTesterPeerApp> Peers;
        }

        Timer _invitesTestTimer = null;
        bool _invitesTestInProgress = false;
        void BeginTestInvitesIfNotStartedAlready()
        {
            if (_invitesTestTimer != null) return;
            _invitesTestTimer = new Timer((obj) =>
            {
                if (_invitesTestInProgress) return;
                _invitesTestInProgress = true;
                var test = new InvitesTest() { Peers = _xList.Where(x => x.LocalDrpPeer != null).ToList() };
                GenerateSharedContactBook(test.Peers);
                BeginTestInvites(test, ()=>
                {
                    _invitesTestInProgress = false;
                });
            }, null, 0, 60000);
        }

        void GenerateSharedContactBook(List<DrpTesterPeerApp> peers)
        {
            var contactBookUsersByRegId = new Dictionary<RegistrationId, UserId>();
            foreach (var x in peers)
                contactBookUsersByRegId.Add(x.LocalDrpPeer.Configuration.LocalPeerRegistrationId, x.UserId);
            foreach (var x in peers)
                x.ContactBookUsersByRegId = contactBookUsersByRegId;
        }

        public ICommand TestInvites => new DelegateCommand(() =>
        {
            var test = new InvitesTest() { Peers = _xList.Where(x => x.LocalDrpPeer != null).ToList() };
            GenerateSharedContactBook(test.Peers);
            BeginTestInvites(test);
        });
    }
}
