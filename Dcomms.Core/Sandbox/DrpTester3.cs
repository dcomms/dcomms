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

        public string VisionChannelSourceIdPrefix { get; set; } = "L_";
        public int NumberOfLocalInterconnectedEpEngines { get; set; } = 0;
        public int NumberOfUserApps { get; set; } = 0;

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
        public ICommand InitializeUser => new DelegateCommand(() =>
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
                _visionChannel.Emit("", DrpTesterVisionChannelModuleName,
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

            if (epEndpoints.Count == 0) throw new Exception("no endpoints for users to register");
           
            var sw = Stopwatch.StartNew();
            _visionChannel.Emit(userEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registering...");
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
                _visionChannel.Emit(userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, 
                    $"{userApp} is connected with {userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, enough to continue to create more users");
                BeginCreateUserAppOrContinue(userIndex + 1);
            }
            else
            {
                _visionChannel.Emit(userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                    $"{userApp} is connected with {userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, not enough to continue with other peers");
                userApp.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(userApp, userIndex);
                }, "userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors4644");
            }
        }

        void BeginTestMessages()
        {

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
