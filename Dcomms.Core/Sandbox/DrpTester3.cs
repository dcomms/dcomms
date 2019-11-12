using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Input;

namespace Dcomms.Sandbox
{
    public class DrpTester3 : BaseNotify, IDisposable
    {
        const int EpAbsoluteMaxDesiredNumberOfNeighbors = 40;
        const int EpSoftMaxDesiredNumberOfNeighbors = 30;
        const int EpMinDesiredNumberOfNeighbors = 13;






        const string DrpTesterVisionChannelModuleName = "drpTester3";
        public ushort LocalInterconnectedEpEnginesBasePort { get; set; } = 12000;
        
        IPEndPoint[] RemoteEpEndPoints = new IPEndPoint[0];
        public string RemoteEpEndPointsString
        {
            get
            {
                if (RemoteEpEndPoints == null) return "";
                return String.Join(";", RemoteEpEndPoints.Select(x => x.ToString()));
            }
            set
            {
                if (String.IsNullOrEmpty(value)) RemoteEpEndPoints = null;
                else RemoteEpEndPoints = (from valueStr in value.Split(';')
                                     let pos = valueStr.IndexOf(':')
                                     where pos != -1
                                     select new IPEndPoint(
                                         IPAddress.Parse(valueStr.Substring(0, pos)),
                                         int.Parse(valueStr.Substring(pos + 1))
                                         )
                        ).ToArray();
            }
        }

        public string VisionChannelSourceIdPrefix { get; set; } = "";
        public int NumberOfLocalInterconnectedEpEngines { get; set; } = 25;
        public int NumberOfUserApps { get; set; } = 50;

        public bool Initialized { get; private set; }

        readonly Random _insecureRandom = new Random();
        readonly List<DrpTesterPeerApp> _localEpApps = new List<DrpTesterPeerApp>();
        readonly List<DrpTesterPeerApp> _userApps = new List<DrpTesterPeerApp>();

        IEnumerable<IVisiblePeer> VisiblePeers
        {
            get
            {
                foreach (var ep in _localEpApps)
                    foreach (var p in ep.DrpPeerEngine.VisibleLocalPeers)
                        yield return p;
                foreach (var u in _userApps)
                    foreach (var p in u.DrpPeerEngine.VisibleLocalPeers)
                        yield return p;
            }
        }

        readonly VisionChannel _visionChannel;
        public DrpTester3(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
            _visionChannel.VisiblePeersDelegate = () => { return VisiblePeers.ToList(); };
        }

        public ICommand InitializeEpHost => new DelegateCommand(() =>
        {
            NumberOfLocalInterconnectedEpEngines = 25;
            RaisePropertyChanged(() => NumberOfLocalInterconnectedEpEngines);
            NumberOfUserApps = 0;
            RaisePropertyChanged(() => NumberOfUserApps);
            RemoteEpEndPointsString = "";
            RaisePropertyChanged(() => RemoteEpEndPointsString);

            Initialize.Execute(null);
        });
        public ICommand InitializeUsers => new DelegateCommand(() =>
        {
            RemoteEpEndPoints = new[] { new IPEndPoint(IPAddress.Parse("195.154.173.208"), 12000) };
            RaisePropertyChanged(() => RemoteEpEndPointsString);
            
            NumberOfLocalInterconnectedEpEngines = 0;
            RaisePropertyChanged(() => NumberOfLocalInterconnectedEpEngines);
            NumberOfUserApps = 50;
            RaisePropertyChanged(() => NumberOfUserApps);
            
            Initialize.Execute(null);
        });
        

        public ICommand Initialize => new DelegateCommand(() =>
        {
            if (Initialized) throw new InvalidOperationException();
            Initialized = true;
            RaisePropertyChanged(() => Initialized);

            // create EPs
            //    interconnect EPs
            // create user apps one by one
            //    connect user app to remote EP (if address specified)
            BeginCreateLocalEpOrContinue(0);         

        });

        void BeginCreateLocalEpOrContinue(int localEpIndex)
        {
            if (localEpIndex >= NumberOfLocalInterconnectedEpEngines)
            { // continue
                _visionChannel.EmitListOfPeers("", DrpTesterVisionChannelModuleName,
                    AttentionLevel.guiActivity, $"connected all EPs");

                foreach (var ep2 in _localEpApps)
                {
                    ep2.DrpPeerRegistrationConfiguration.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = null;
                    ep2.DrpPeerRegistrationConfiguration.AbsoluteMaxNumberOfNeighbors = EpAbsoluteMaxDesiredNumberOfNeighbors;
                    ep2.DrpPeerRegistrationConfiguration.SoftMaxNumberOfNeighbors = EpSoftMaxDesiredNumberOfNeighbors;
                    ep2.DrpPeerRegistrationConfiguration.MinDesiredNumberOfNeighbors = EpMinDesiredNumberOfNeighbors;
                }

                BeginCreateUserAppOrContinue(0);
                return;
            }


            var epEngine = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                LocalPort = (ushort)(LocalInterconnectedEpEnginesBasePort + localEpIndex),
                VisionChannel = _visionChannel,
                VisionChannelSourceId = $"{VisionChannelSourceIdPrefix}EP{localEpIndex}",
            }); ;
            var epLocalDrpPeerConfig = LocalDrpPeerConfiguration.CreateWithNewKeypair(epEngine.CryptoLibrary);
            epLocalDrpPeerConfig.MinDesiredNumberOfNeighbors = null;
            epLocalDrpPeerConfig.AbsoluteMaxNumberOfNeighbors = null;
            epLocalDrpPeerConfig.SoftMaxNumberOfNeighbors = null;
            epLocalDrpPeerConfig.MinDesiredNumberOfNeighborsSatisfied_WorstNeighborDestroyIntervalS = null;
            var epApp = new DrpTesterPeerApp(epEngine, epLocalDrpPeerConfig);

            epEngine.BeginCreateLocalPeer(epLocalDrpPeerConfig, epApp, (localDrpPeer) =>
            {
                epApp.LocalDrpPeer = localDrpPeer;
                
                var connectToEpsList = _localEpApps.ToList();
                _localEpApps.Add(epApp);
                _visionChannel.Emit(epEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                    AttentionLevel.guiActivity, $"created local EP #{localEpIndex}/{NumberOfLocalInterconnectedEpEngines}");

                if (connectToEpsList.Count != 0)
                {
                    _visionChannel.Emit(epEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        AttentionLevel.guiActivity, $"connecting with {connectToEpsList.Count} other EPs...");

                    var connectToEpEndpoints = new List<IPEndPoint>();
                    foreach (var connectToEp in connectToEpsList)
                        connectToEpEndpoints.Add(                            
                                new IPEndPoint(connectToEp.LocalDrpPeer.PublicIpApiProviderResponse, 
                                    connectToEp.DrpPeerEngine.Configuration.LocalPort.Value));
                    
                    epApp.LocalDrpPeer.BeginConnectToEPs(connectToEpEndpoints.ToArray(), () =>
                    {
                        BeginCreateLocalEpOrContinue(localEpIndex + 1);
                    });
                }
                else
                {
                    BeginCreateLocalEpOrContinue(localEpIndex + 1);
                }
            });

        }

        void BeginCreateUserAppOrContinue(int userIndex)
        {
            if (userIndex >= NumberOfUserApps)
            {
                BeginTestMessages();
                return;
            }

            var userEngine = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                VisionChannel = _visionChannel,
                VisionChannelSourceId = $"{VisionChannelSourceIdPrefix}U{userIndex}",
            });
            var localDrpPeerConfiguration = LocalDrpPeerConfiguration.CreateWithNewKeypair(userEngine.CryptoLibrary);

            var epEndpoints = RemoteEpEndPoints.ToList();
            epEndpoints.AddRange(_localEpApps.Select(x => new IPEndPoint(x.LocalDrpPeer.PublicIpApiProviderResponse, x.DrpPeerEngine.Configuration.LocalPort.Value)));
            localDrpPeerConfiguration.EntryPeerEndpoints = epEndpoints.ToArray();

            var userApp = new DrpTesterPeerApp(userEngine, localDrpPeerConfiguration);
            _userApps.Add(userApp);
            if (epEndpoints.Count == 0) throw new Exception("no endpoints for users to register");
           
            var sw = Stopwatch.StartNew();
            _visionChannel.Emit(userEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registering (adding first neighbor)... via {epEndpoints.Count} EPs");
            userEngine.BeginRegister(localDrpPeerConfiguration, userApp, (localDrpPeer) =>
            {
                userApp.LocalDrpPeer = localDrpPeer;
                _visionChannel.Emit(userEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");

                // wait until number of neighbors reaches minimum
                userEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(userApp, userIndex);
                }, "waiting for connection with neighbors 324155");
            });           
        }
        void userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(DrpTesterPeerApp userApp, int userIndex)
        {
            if (userApp.LocalDrpPeer.ConnectedNeighbors.Count >= userApp.LocalDrpPeer.Configuration.MinDesiredNumberOfNeighbors)
            {
                _visionChannel.EmitListOfPeers(userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, 
                    $"{userApp} is connected with {userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, enough to continue to create more users");
                BeginCreateUserAppOrContinue(userIndex + 1);
            }
            else
            {
                _visionChannel.Emit(userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                    $"{userApp} is connected with {userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, not enough to continue with more users");
                userApp.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(userApp, userIndex);
                }, "userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors4644");
            }
        }

        void BeginTestMessages()
        {
            var contactBookUsersByRegId = new Dictionary<RegistrationId, UserId>();
            foreach (var u in _userApps)
                contactBookUsersByRegId.Add(u.LocalDrpPeer.Configuration.LocalPeerRegistrationId, u.UserId);
            foreach (var u in _userApps)
                u.ContactBookUsersByRegId = contactBookUsersByRegId;

            BeginTestMessage(new MessagesTest());
        }
        class MessagesTest
        {
            public int SentCount = 0;
            public int SuccessfulCount = 0;
        }

        void BeginTestMessage(MessagesTest test)
        {
            var peer1 = _userApps[test.SentCount++ % _userApps.Count];

        _retry:
            var peer2 = _userApps[_insecureRandom.Next(_userApps.Count)];
            if (peer1 == peer2) goto _retry;

            _visionChannel.EmitListOfPeers(peer1.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                    $"testing message #{test.SentCount} from {peer1} to {peer2}");

            var userCertificate1 = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(peer1.DrpPeerEngine.CryptoLibrary, peer1.UserId, peer1.UserRootPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

            var text = $"test{_insecureRandom.Next()}-{_insecureRandom.Next()}_from_{peer1}_to_{peer2}";
            peer1.LocalDrpPeer.BeginSendShortSingleMessage(userCertificate1, peer2.LocalDrpPeer.Configuration.LocalPeerRegistrationId, peer2.UserId, text, (exc) =>
            {
                if (peer2.LatestReceivedTextMessage == text)
                {
                    test.SuccessfulCount++;
                    _visionChannel.EmitListOfPeers(peer1.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        AttentionLevel.guiActivity, $"successfully tested message from {peer1} to {peer2}. " +
                        $"success rate = {test.SuccessfulCount * 100 / test.SentCount}% ({test.SuccessfulCount}/{test.SentCount})");
                }
                else
                {
                    _visionChannel.EmitListOfPeers(peer1.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                       AttentionLevel.mediumPain, $"test message failed from {peer1} to {peer2}: received '{peer2.LatestReceivedTextMessage}', expected '{text}. " +
                        $"success rate = {test.SuccessfulCount * 100 / test.SentCount}% ({test.SuccessfulCount}/{test.SentCount})");
                }

                BeginTestMessage(test);            
            });
        }



        public void Dispose()
        {
            foreach (var ep in _localEpApps)
                ep.DrpPeerEngine.Dispose();
            foreach (var u in _userApps)
                u.DrpPeerEngine.Dispose();
        }
    }
}
