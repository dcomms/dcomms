using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
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
        void BeginDisposeOnFailure() // if not alrady disposing
        {
            if (_disposingOnFailure) return;
            _disposingOnFailure = true;

            var a = new Action(() =>
            {
                Dispose();
            });
            a.BeginInvoke((ar) => a.EndInvoke(ar), null);
        }
        bool _disposingOnFailure;


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
                RegistrationId_ed25519privateKey = new byte[] { 0x42, 0xFF, 0x59, 0x72, 0x3C, 0x2D, 0x82, 0xC6, 0x4E, 0xC3, 0x97, 0x3F, 0xB7, 0x4A, 0x57, 0x18, 0xD7, 0x23, 0x58, 0x6D, 0x88, 0x95, 0x85, 0x69, 0x65, 0x6A, 0xAB, 0x8F, 0xC8, 0xD5, 0xB2, 0xD9 },
                RegistrationId = new RegistrationId(new byte[] { 0x46, 0xE2, 0x7E, 0xFF, 0x20, 0x92, 0x36, 0x43, 0xD4, 0xD8, 0xA8, 0x47, 0x8E, 0x75, 0xA5, 0xF0, 0xAE, 0x26, 0x70, 0x0D, 0x7B, 0x41, 0xD4, 0xB5, 0x21, 0xDB, 0x2B, 0xFB, 0x6C, 0x8F, 0x21, 0xB4 }),
                UserId = new UserId() 
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0x7D, 0xA2, 0x37, 0x5E, 0xC7, 0xE1, 0x4C, 0x28, 0xCA, 0x65, 0xA3, 0x81, 0x86, 0x2E, 0x1B, 0x49, 0x38, 0x58, 0xD6, 0x7B, 0x09, 0xF7, 0x9B, 0x29, 0x66, 0xE3, 0x68, 0xBD, 0x74, 0xC3, 0x4E, 0xB6},
                        new byte[] { 0x13, 0x40, 0xED, 0xC0, 0x6F, 0x2D, 0x6A, 0x7B, 0x15, 0xD4, 0xCE, 0x27, 0xF0, 0xAC, 0xCA, 0xEB, 0xB6, 0xE2, 0x5D, 0x96, 0x8E, 0x8A, 0x35, 0x97, 0x40, 0x1A, 0x5B, 0x97, 0x4C, 0xE0, 0x03, 0x92},
                        new byte[] { 0x10, 0x7B, 0x43, 0x48, 0x98, 0xBE, 0xFC, 0x68, 0x26, 0x48, 0xAE, 0xAE, 0x56, 0xD8, 0x48, 0xAB, 0xE2, 0x69, 0x2F, 0x50, 0x4F, 0x70, 0xDE, 0x12, 0xC2, 0xB9, 0xE8, 0x65, 0xE4, 0x7D, 0xBE, 0x23},
                    }
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                    ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0xBE, 0xB1, 0xC5, 0x72, 0x6B, 0x17, 0x17, 0x25, 0x9A, 0x88, 0x9B, 0xE6, 0xD7, 0xD2, 0xE3, 0x16, 0x6B, 0xFE, 0xFC, 0x9D, 0xBE, 0x61, 0x36, 0x42, 0xF8, 0x92, 0x94, 0xBB, 0x56, 0xFC, 0x28, 0x0D},
                        new byte[] { 0x54, 0x55, 0xE6, 0xEB, 0x41, 0xAF, 0xD9, 0x3C, 0xF0, 0x7B, 0x16, 0x80, 0x47, 0xDD, 0xC6, 0x13, 0x0F, 0x6C, 0xE0, 0xD2, 0x26, 0x0E, 0x40, 0x94, 0x3E, 0x72, 0x40, 0x04, 0xC5, 0x0E, 0x85, 0x1D},
                        new byte[] { 0x15, 0xF2, 0xFF, 0x88, 0x57, 0x70, 0x53, 0xE8, 0xC1, 0xBF, 0x7B, 0x59, 0x3B, 0x30, 0x9F, 0x74, 0x6A, 0xBF, 0x2D, 0xD2, 0x7C, 0x3D, 0x9C, 0xD5, 0xC8, 0x40, 0x7A, 0xD2, 0xED, 0x47, 0x17, 0x29},
                        }
                }
            },
            new PredefinedUser {
                Name = "02",
                RegistrationId_ed25519privateKey = new byte[] { 0xDA, 0x52, 0x8B, 0x37, 0x80, 0xC5, 0x29, 0x61, 0x44, 0x59, 0x8D, 0x52, 0x59, 0x56, 0x78, 0x25, 0x0F, 0x91, 0xC6, 0x60, 0x66, 0x57, 0xE5, 0x5F, 0x23, 0xB9, 0x87, 0xF1, 0xCB, 0xB2, 0x08, 0xB8 },
                RegistrationId = new RegistrationId(new byte[] { 0x6E, 0xE9, 0x92, 0xA2, 0xAB, 0xA9, 0x31, 0x79, 0x99, 0xEA, 0xF9, 0x1C, 0xA6, 0x43, 0x34, 0xF7, 0x00, 0x2E, 0xAE, 0x32, 0xF6, 0x29, 0x54, 0xF2, 0x58, 0x9F, 0xFE, 0x5A, 0x61, 0x15, 0x81, 0x12 }),
                UserId = new UserId()
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0x64, 0x32, 0x01, 0x7F, 0xDC, 0x19, 0xD6, 0xE3, 0x69, 0xE7, 0x56, 0x74, 0x04, 0x8C, 0xD6, 0x15, 0xAA, 0x4F, 0xEF, 0xC6, 0x7A, 0x24, 0xA9, 0x28, 0xC0, 0x05, 0x3B, 0x84, 0xDF, 0x87, 0xAD, 0x3A},
                        new byte[] { 0xA9, 0x83, 0xDE, 0x5B, 0x49, 0x89, 0x41, 0x96, 0xA5, 0x46, 0x6B, 0xAB, 0xBC, 0xC7, 0x49, 0x52, 0x36, 0x5B, 0xAD, 0xD8, 0x3C, 0x1A, 0x93, 0xB5, 0x15, 0x85, 0x7C, 0xEE, 0x8B, 0x18, 0x79, 0x6D},
                        new byte[] { 0x67, 0x8B, 0x2F, 0xF2, 0xF3, 0x4D, 0x38, 0xFA, 0x8B, 0x5B, 0xA6, 0xFF, 0xC5, 0xC2, 0x0D, 0x22, 0xF6, 0x69, 0xD8, 0x0C, 0xDE, 0xBF, 0x87, 0x35, 0x04, 0x9B, 0x21, 0x15, 0x75, 0xBE, 0xDF, 0x84},
                        }
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                   ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0x56, 0x99, 0x55, 0x0D, 0xD0, 0xB2, 0xD6, 0xAA, 0x9B, 0x87, 0x15, 0x31, 0xEB, 0xD2, 0xA9, 0xB7, 0x1A, 0xAE, 0xD9, 0x17, 0x8B, 0x15, 0x96, 0x10, 0x70, 0x8D, 0x8E, 0x39, 0x5C, 0x5F, 0x60, 0x5C},
                        new byte[] { 0x1B, 0x37, 0xCD, 0x22, 0xDE, 0x86, 0xEF, 0xA9, 0x43, 0x72, 0xD2, 0x8F, 0x76, 0x54, 0xF9, 0xD3, 0xA7, 0x38, 0xD8, 0xC4, 0x59, 0x17, 0x91, 0xEB, 0xE6, 0x59, 0x41, 0x3F, 0xE2, 0x39, 0x28, 0xA0},
                        new byte[] { 0xA8, 0x6F, 0x0B, 0x08, 0xF8, 0x5E, 0x74, 0x7D, 0x56, 0xCC, 0x51, 0x82, 0x94, 0x6B, 0xF5, 0x6D, 0x49, 0x61, 0x53, 0x42, 0x35, 0x78, 0x6B, 0xC0, 0xBB, 0xA5, 0x28, 0x7C, 0x0E, 0xE8, 0x46, 0x5D},
                        }
                }
            },
            new PredefinedUser {
                Name = "03",
                RegistrationId_ed25519privateKey = new byte[] { 0x45, 0xBB, 0x47, 0xDE, 0x6A, 0xDA, 0x22, 0x56, 0x23, 0xC7, 0x7F, 0x7F, 0xB2, 0x5F, 0xAE, 0x09, 0x43, 0x90, 0x79, 0x54, 0xBF, 0x3F, 0x7B, 0xD6, 0xB7, 0xF3, 0x6A, 0xAF, 0x15, 0xE7, 0xEC, 0x36 },
                RegistrationId = new RegistrationId(new byte[] { 0x4B, 0x9A, 0x46, 0x56, 0xB5, 0x6F, 0x7C, 0xA5, 0xCB, 0x1D, 0x7D, 0xB8, 0x8A, 0xE7, 0x31, 0x14, 0xFF, 0x21, 0x4C, 0x43, 0xEA, 0x57, 0x67, 0x0D, 0xF3, 0x5F, 0xE1, 0xFA, 0x59, 0x5A, 0x1F, 0x2F }),
                UserId = new UserId()
                {
                   RootPublicKeys = new List<byte[]> {
                        new byte[] { 0xB2, 0x2F, 0xF4, 0x43, 0xE1, 0x16, 0xF1, 0x80, 0x2C, 0x69, 0x00, 0x80, 0xB2, 0xB3, 0xD6, 0xC0, 0x40, 0x97, 0xE0, 0x48, 0x93, 0xAB, 0x13, 0xD9, 0xDD, 0x33, 0xB9, 0x5B, 0xDA, 0xCA, 0xAD, 0x96},
                        new byte[] { 0xC5, 0x8F, 0x61, 0x95, 0x1F, 0xAA, 0x67, 0x6E, 0x6E, 0x3C, 0x16, 0x5D, 0x3D, 0x14, 0xFB, 0xE3, 0x44, 0x3D, 0x2D, 0xD9, 0xD5, 0x30, 0x28, 0xCC, 0x79, 0xD6, 0x59, 0xE4, 0x51, 0x62, 0x9A, 0x02},
                        new byte[] { 0xE7, 0xAA, 0x37, 0x15, 0x66, 0xA1, 0x48, 0x4C, 0x9E, 0xEB, 0x01, 0x0E, 0x6B, 0xBB, 0x36, 0xC3, 0xA4, 0x50, 0xD6, 0xE2, 0x71, 0xD3, 0xF1, 0x83, 0x14, 0x78, 0x48, 0x0A, 0x68, 0x99, 0x8D, 0x1A},
                        }
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                   ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0x46, 0x52, 0x9C, 0xCF, 0xEF, 0x6B, 0xD7, 0x2F, 0x9D, 0x1E, 0xAA, 0xA5, 0xA9, 0x13, 0xFC, 0x08, 0xB1, 0x40, 0xB6, 0x68, 0x4A, 0x11, 0x24, 0xA2, 0x40, 0x69, 0x68, 0x25, 0x57, 0x2D, 0xE2, 0x7C},
                        new byte[] { 0x88, 0x66, 0x56, 0x68, 0x7B, 0xCB, 0xA4, 0xFC, 0xD6, 0xA9, 0xAB, 0x07, 0x79, 0x8E, 0xBD, 0xB8, 0xB1, 0x0F, 0xE9, 0xFF, 0x22, 0x2D, 0xC9, 0x8D, 0xEA, 0xDA, 0x28, 0xCD, 0x9A, 0xB1, 0xA2, 0xC6},
                        new byte[] { 0x72, 0x7D, 0xE3, 0x28, 0xE3, 0xA9, 0x84, 0xD8, 0x29, 0xDE, 0xD5, 0x81, 0xBB, 0x22, 0xEB, 0x9D, 0xAC, 0x69, 0xE0, 0x38, 0x58, 0xE8, 0x73, 0xDA, 0x97, 0x29, 0xD5, 0x89, 0xFE, 0x23, 0x06, 0x97},
                        }
                }
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
            
            var localDrpPeerConfiguration = LocalDrpPeerConfiguration.Create(userEngine.CryptoLibrary, NumberOfDimensions, 
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
                    AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(waitForNeighborsSw);
                }, "waiting for connection with neighbors 324155");
            });
        });
        void AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(Stopwatch waitForNeighborsSw)
        {
            if (_userApp.LocalDrpPeer.ConnectedNeighbors.Count >= _userApp.LocalDrpPeer.Configuration.MinDesiredNumberOfNeighbors)
            {
                waitForNeighborsSw.Stop();

                var level = waitForNeighborsSw.Elapsed.TotalMilliseconds < 10000 ? AttentionLevel.guiActivity : AttentionLevel.lightPain;
                _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity,
                    $"{_userApp} is connected with {_userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors (in {waitForNeighborsSw.Elapsed.TotalMilliseconds}ms), enough to continue");

                SendMessage();
            }
            else
            {
                _visionChannel.Emit(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.higherLevelDetail,
                    $"{_userApp} is connected with {_userApp.LocalDrpPeer.ConnectedNeighbors.Count} neighbors, not enough to continue with more users");
                _userApp.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromSeconds(1), () =>
                {
                    AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(waitForNeighborsSw);
                }, "userEngine_AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors53809");
            }
        }
        void SendMessage()
        {
            // send msg (with autoRetry=true)   wait for completion
            var userCertificate1 = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_userApp.DrpPeerEngine.CryptoLibrary, _userApp.UserId,
                _userApp.UserRootPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            var sentText = $"{DrpTesterPeerApp.EchoTestPrefix}_#{_sentCount}_{VisionChannelSourceId}_from_{LocalUser.Name}_to_{RemoteUser.Name}_{_insecureRandom.Next()}";
            var sw = Stopwatch.StartNew();
            OnSent();
            _userApp.LocalDrpPeer.BeginSendShortSingleMessage(userCertificate1, RemoteUser.RegistrationId, RemoteUser.UserId, sentText, TimeSpan.FromSeconds(60),
                (exc) =>
                {
                    if (exc != null)
                    {
                        _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.strongPain,
                            $"could not send message: {exc}");

                        if (!ContinueOnFailed()) return;
                        SendMessage();
                    }
                    else
                        BeginVerifyReceivedEchoedMessage(sentText, sw, Stopwatch.StartNew());
                });
        }

        void BeginVerifyReceivedEchoedMessage(string sentText, Stopwatch sw, Stopwatch afterSendingCompletedSw)
        {
            if (_userApp.LatestReceivedTextMessage == sentText)
            {
                sw.Stop();
                OnSuccessfullyDelivered(sw.Elapsed.TotalMilliseconds, _visionChannel.TimeNow, _userApp.LatestReceivedTextMessage_req);
                _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                    AttentionLevel.guiActivity, $"successfully received echoed message in {sw.Elapsed.TotalMilliseconds}ms. {TestReport}");
            }
            else
            { // try to wait for 1 sec   in case when sender-side callback is invoked BEFORE receiver-side callback
                if (afterSendingCompletedSw.Elapsed.TotalSeconds < 60)
                {
                    _userApp.DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromMilliseconds(20), () =>
                    {
                        BeginVerifyReceivedEchoedMessage(sentText, sw, afterSendingCompletedSw);
                    }, "verifyMsg 5893");
                    return;
                }
                
                _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                   AttentionLevel.strongPain,
                   $"test message failed: received '{_userApp.LatestReceivedTextMessage}', expected '{sentText}. {TestReport}");

                if (!ContinueOnFailed()) return;
            }

            SendMessage(); // continue with next test message
        }
        bool ContinueOnFailed()
        {
            var failedCount = OnFailed(_visionChannel.TimeNow);
            if (failedCount >= 1)
            {
                _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                          AttentionLevel.strongPain,
                          $"disposing the test: {failedCount} messages failed");

                BeginDisposeOnFailure();
                return false;
            }
            return true;
        }
                     
        #region  test results
        
        int _sentCount = 0;
        public int OnSent()
        {
            return _sentCount++;
        }
        int SuccessfulCount = 0;

        double _delaysSumMs = 0;
        double MaxDelayMs;
        DateTime MaxDelayTime;
        double AvgDelayMs => _delaysSumMs / SuccessfulCount;

        double _numberOfHopsRemainingSum;
        double AvgNumberOfHopsRemaining => _numberOfHopsRemainingSum / SuccessfulCount;
        int MinNumberOfHopsRemaining = InviteRequestPacket.MaxNumberOfHopsRemaining;
        DateTime MinNumberOfHopsRemainingTime;

        public string TestReport => $"success rate = {(double)SuccessfulCount * 100 / _sentCount}% ({SuccessfulCount}/{_sentCount}) " +
                $"delay: avg={AvgDelayMs}ms, max={MaxDelayMs} at {MaxDelayTime.ToString("dd-HH:mm:ss.fff")}\r\n" +
                $"nHopsRemaining: avg={AvgNumberOfHopsRemaining}, min={MinNumberOfHopsRemaining} at {MinNumberOfHopsRemainingTime.ToString("dd-HH:mm:ss.fff")}\r\n" +
            $"failures: {_failedCount}; last: {_lastFailureTime?.ToString("dd-HH:mm:ss.fff")}";

        public void OnSuccessfullyDelivered(double delayMs, DateTime now, InviteRequestPacket req)
        {
            SuccessfulCount++;
            _delaysSumMs += delayMs;
            if (delayMs > MaxDelayMs)
            {
                MaxDelayMs = delayMs;
                MaxDelayTime = now;
            }

            _numberOfHopsRemainingSum += req.NumberOfHopsRemaining;

            if (req.NumberOfHopsRemaining < MinNumberOfHopsRemaining)
            {
                MinNumberOfHopsRemaining = req.NumberOfHopsRemaining;
                MinNumberOfHopsRemainingTime = now;
            }
        }


        DateTime? _lastFailureTime;
        public int _failedCount;
        public int OnFailed(DateTime now)
        {
            _lastFailureTime = now;
            return ++_failedCount;
        }
       
        #endregion
    }
}
