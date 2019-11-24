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
    public class DrpTester5 : BaseNotify, IDisposable
    {

        const string DrpTesterVisionChannelModuleName = "drpTester5";

        public bool Initialized { get; private set; }

        public string VisionChannelSourceId { get; set; } = "U";
        public int NumberOfDimensions { get; set; } = 8;
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

        readonly Random _insecureRandom = new Random();
        DrpTesterPeerApp _userApp;

        IEnumerable<IVisiblePeer> VisiblePeers
        {
            get
            {
                var r = new List<LocalDrpPeer>();
                try
                {
                    if (_userApp != null)
                        foreach (var p in _userApp.DrpPeerEngine.VisibleLocalPeers)
                            r.Add(p);
                }
                catch (Exception exc)
                {
                    _visionChannel.Emit("", DrpTesterVisionChannelModuleName,
                            AttentionLevel.mediumPain, $"error when getting visible peers: {exc}");
                }

                var existingRegIDs = new HashSet<RegistrationId>(r.Select(x => x.Configuration.LocalPeerRegistrationId));
                foreach (var p in r)
                {
                    yield return p;
                    foreach (var neighbor in p.ConnectedNeighborsCanBeUsedForNewRequests)
                        if (!existingRegIDs.Contains(neighbor.RemoteRegistrationId))
                        {
                            existingRegIDs.Add(neighbor.RemoteRegistrationId);
                            yield return neighbor;
                        }
                }
            }
        }

        readonly VisionChannel _visionChannel;
        public DrpTester5(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
            _visionChannel.VisiblePeersDelegate = () => { return VisiblePeers.ToList(); };
            RemoteEpEndPointsString = "192.99.160.225:12000";
            
            LocalUser = PredefinedUsers[0];
            RemoteUser = PredefinedUsers[1];
        }
                     
        public void Dispose()
        {
            if (_userApp != null)
                _userApp.DrpPeerEngine.Dispose();
        }

        public class PredefinedUser
        {
            public string Name { get; set; }
            public byte[] RegistrationId_ed25519privateKey;
            public RegistrationId RegistrationId;
            public UserRootPrivateKeys UserRootPrivateKeys;
            public UserId UserId;
        }
        public List<PredefinedUser> PredefinedUsers { get; set; } = new List<PredefinedUser>
        {
            new PredefinedUser {
                Name = "01",
                RegistrationId_ed25519privateKey = new byte[] { 0x02, 0x76, 0x76 },
                RegistrationId = new RegistrationId(new byte[] { 0x01, 0x26, 0x76 }),
                UserId = null,
                UserRootPrivateKeys = null,
            },
            new PredefinedUser {
                Name = "02",
                RegistrationId_ed25519privateKey = new byte[] { 0x02, 0x76, 0x76 },
                RegistrationId = new RegistrationId(new byte[] { 0x01, 0x26, 0x76 }),
                UserId = null,
                UserRootPrivateKeys = null,
            }
        };
        public PredefinedUser LocalUser { get; set; }
        public PredefinedUser RemoteUser { get; set; }


        public ICommand Initialize => new DelegateCommand(() =>
        {
            if (Initialized) throw new InvalidOperationException();
            Initialized = true;
            RaisePropertyChanged(() => Initialized);
            
            var userEngine = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                VisionChannel = _visionChannel,
                VisionChannelSourceId = VisionChannelSourceId,
                SandboxModeOnly_NumberOfDimensions = NumberOfDimensions
            });
            var localDrpPeerConfiguration = LocalDrpPeerConfiguration.CreateWithNewKeypair(userEngine.CryptoLibrary, NumberOfDimensions, 
                LocalUser.RegistrationId_ed25519privateKey, LocalUser.RegistrationId);

            var epEndpoints = RemoteEpEndPoints.ToList();
            localDrpPeerConfiguration.EntryPeerEndpoints = RemoteEpEndPoints;

            _userApp = new DrpTesterPeerApp(userEngine, localDrpPeerConfiguration, LocalUser.UserRootPrivateKeys, LocalUser.UserId);

            var contactBookUsersByRegId = new Dictionary<RegistrationId, UserId>();
            foreach (var u in PredefinedUsers)
                contactBookUsersByRegId.Add(u.RegistrationId, u.UserId);
            _userApp.ContactBookUsersByRegId = contactBookUsersByRegId;
            
            if (epEndpoints.Count == 0) throw new Exception("no endpoints for users to register");

            var sw = Stopwatch.StartNew();
            _visionChannel.Emit(userEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registering (adding first neighbor)... via {epEndpoints.Count} EPs");
            userEngine.BeginRegister(localDrpPeerConfiguration, _userApp, (localDrpPeer) =>
            {
                _userApp.LocalDrpPeer = localDrpPeer;
                _visionChannel.Emit(userEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
                var waitForNeighborsSw = Stopwatch.StartNew();

                // wait until number of neighbors reaches minimum
                userEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromMilliseconds(300), () =>
                {
                    userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(waitForNeighborsSw);
                }, "waiting for connection with neighbors 324155");
            });
        });
        void userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(Stopwatch waitForNeighborsSw)
        {
            if (_userApp.LocalDrpPeer.ConnectedNeighbors.Count >= _userApp.LocalDrpPeer.Configuration.MinDesiredNumberOfNeighbors)
            {
                waitForNeighborsSw.Stop();

                var level = waitForNeighborsSw.Elapsed.TotalMilliseconds < 10000 ? AttentionLevel.guiActivity : AttentionLevel.lightPain;
                _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                    $"{_userApp} is connected with {_userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors (in {waitForNeighborsSw.Elapsed.TotalMilliseconds}ms), enough to continue");

                userEngine_ContinueWhenConnectedToEnoughNeighbors();
            }
            else
            {
                _visionChannel.Emit(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.higherLevelDetail,
                    $"{_userApp} is connected with {_userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, not enough to continue with more users");
                _userApp.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(waitForNeighborsSw);
                }, "userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors53809");
            }
        }
        void userEngine_ContinueWhenConnectedToEnoughNeighbors()
        {
            
        }
    }
}
