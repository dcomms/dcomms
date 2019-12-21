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
    public class DrpTester5 : BaseNotify, IDisposable, IVisibleModule
    {

        const string DrpTesterVisionChannelModuleName = "drpTester5";

        public bool Initialized { get; private set; }
        public string LocalUdpPortString { get; set; }
        public string MaxNeighborsCountString { get; set; }
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

        #region NAT test #1
        public bool Nat1TestStarted { get; set; }
        public DelegateCommand StartNat1Test => new DelegateCommand(() =>
        {
            Nat1TestStarted = true;
            RaisePropertyChanged(() => Nat1TestStarted);
            TestNat1Async(_userApp.DrpPeerEngine);
        });
        public DelegateCommand StopNat1Test => new DelegateCommand(() =>
        {
            Nat1TestStarted = false;
            RaisePropertyChanged(() => Nat1TestStarted);
        });
        public string Nat1TestRemoteEPs { get; set; } = "192.99.160.225:12000-12016\r\n195.154.173.208:12000-12016\r\n5.135.179.50:12000-12016";
        
        public string Nat1TestTTL { get; set; }
        public string Nat1TestWaitTimeMs { get; set; } = "1500";

        async void TestNat1Async(DrpPeerEngine drpPeerEngine)
        {
            _visionChannel.Emit("NATtest1", "NATtest1", AttentionLevel.guiActivity, $"running NAT#1 test....");
          
            for (int loop = 0; ; loop++)
            {
                if (!Nat1TestStarted) break;
                if (drpPeerEngine.IsDisposed) break;

                await drpPeerEngine.EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(20), "nattest1 528");

                short? ttl = null;
                if (short.TryParse(Nat1TestTTL, out var ttl2)) ttl = ttl2;
                var rnd = new Random();
                var epLines = Nat1TestRemoteEPs.Split(new[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var epLine in epLines)
                {                  
                    var i0 = epLine.IndexOf(':');
                    if (i0 == -1) continue;
                    var ipStr = epLine.Substring(0, i0);
                    if (!IPAddress.TryParse(ipStr, out var epIP)) continue;
                    var portsStr = epLine.Substring(i0 + 1);
                    int portsRangeStart, portsRangeEnd; // inclusive
                    var i1 = portsStr.IndexOf('-');
                    if (i1 == -1)
                    {
                        if (!Int32.TryParse(portsStr, out var port1)) continue;
                        portsRangeStart = portsRangeEnd = port1;
                    }
                    else
                    {
                        if (!Int32.TryParse(portsStr.Substring(0, i1), out portsRangeStart)) continue;
                        if (!Int32.TryParse(portsStr.Substring(i1 + 1), out portsRangeEnd)) continue;
                        if (portsRangeEnd <= portsRangeStart) continue;
                    }
                    var port = portsRangeStart + loop % (portsRangeEnd - portsRangeStart + 1);

                    var responderEp = new IPEndPoint(epIP, port);
                    var req = new NatTest1RequestPacket { Token32 = (uint)rnd.Next() };
                    var sw = Stopwatch.StartNew();
                    var nateTest1responseData = await drpPeerEngine.SendUdpRequestAsync_Retransmit(new PendingLowLevelUdpRequest("nattest1 23",
                        responderEp
                        , NatTest1ResponsePacket.GetScanner(req.Token32),
                        drpPeerEngine.DateTimeNowUtc,
                        1.0,
                        req.Encode(),
                        0.3,
                        1.2
                        )
                    { TTL = ttl });
                    if (nateTest1responseData != null)
                    {
                        var nateTest1response = NatTest1ResponsePacket.Decode(nateTest1responseData);
                        _visionChannel.Emit("NATtest1", "NATtest1", AttentionLevel.guiActivity, $"response from {responderEp}: {nateTest1response.RequesterEndpoint} {(int)sw.Elapsed.TotalMilliseconds}ms");
                    }
                    else
                        _visionChannel.Emit("NATtest1", "NATtest1", AttentionLevel.guiActivity, $"no response from {responderEp}");

                }
                if (int.TryParse(Nat1TestWaitTimeMs, out var sleepTimeMs))
                {
                    await drpPeerEngine.EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(sleepTimeMs), "nattest1 247");
                }
            }
        }
        #endregion
        
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
            RemoteEpEndPointsString = "192.99.160.225:12000;195.154.173.208:12000;5.135.179.50:12000";
            
            LocalUser = PredefinedUsers[0];
            RemoteUser = PredefinedUsers[1];
        }
                     
        public void Dispose()
        {
            if (_userApp != null)
                _userApp.DrpPeerEngine.Dispose();
        }

        public ICommand DeinitializeDrpPeer => new DelegateCommand(() =>
        {
            if (Initialized)
            {
                if (_userApp != null)
                    _userApp.DrpPeerEngine.DisposeDrpPeers();
            }
        });
        public ICommand Deinitialize => new DelegateCommand(() =>
        {
            if (Initialized)
            {
                if (_userApp != null)
                    _userApp.DrpPeerEngine.Dispose();

                Initialized = false;
                RaisePropertyChanged(() => Initialized);
                Nat1TestStarted = false;
                RaisePropertyChanged(() => Nat1TestStarted);
            }
        });

        public class PredefinedUser
        {
            public string Name { get; set; }
            public byte[] RegistrationId_ed25519privateKey;
            public RegistrationId RegistrationId;
            public UserRootPrivateKeys UserRootPrivateKeys;
            public UserId UserId;
            public bool SendOrEcho;
        }
        public List<PredefinedUser> PredefinedUsers { get; set; } = new List<PredefinedUser>
        {
            new PredefinedUser {
                SendOrEcho = true,
                Name = "01S",
                RegistrationId_ed25519privateKey = new byte[] { 0x42, 0xFF, 0x59, 0x72, 0x3C, 0x2D, 0x82, 0xC6, 0x4E, 0xC3, 0x97, 0x3F, 0xB7, 0x4A, 0x57, 0x18, 0xD7, 0x23, 0x58, 0x6D, 0x88, 0x95, 0x85, 0x69, 0x65, 0x6A, 0xAB, 0x8F, 0xC8, 0xD5, 0xB2, 0xD9 },
                RegistrationId = new RegistrationId(new byte[] { 0x46, 0xE2, 0x7E, 0xFF, 0x20, 0x92, 0x36, 0x43, 0xD4, 0xD8, 0xA8, 0x47, 0x8E, 0x75, 0xA5, 0xF0, 0xAE, 0x26, 0x70, 0x0D, 0x7B, 0x41, 0xD4, 0xB5, 0x21, 0xDB, 0x2B, 0xFB, 0x6C, 0x8F, 0x21, 0xB4 }),
                UserId = new UserId() 
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0x7D, 0xA2, 0x37, 0x5E, 0xC7, 0xE1, 0x4C, 0x28, 0xCA, 0x65, 0xA3, 0x81, 0x86, 0x2E, 0x1B, 0x49, 0x38, 0x58, 0xD6, 0x7B, 0x09, 0xF7, 0x9B, 0x29, 0x66, 0xE3, 0x68, 0xBD, 0x74, 0xC3, 0x4E, 0xB6},
                        new byte[] { 0x13, 0x40, 0xED, 0xC0, 0x6F, 0x2D, 0x6A, 0x7B, 0x15, 0xD4, 0xCE, 0x27, 0xF0, 0xAC, 0xCA, 0xEB, 0xB6, 0xE2, 0x5D, 0x96, 0x8E, 0x8A, 0x35, 0x97, 0x40, 0x1A, 0x5B, 0x97, 0x4C, 0xE0, 0x03, 0x92},
                        new byte[] { 0x10, 0x7B, 0x43, 0x48, 0x98, 0xBE, 0xFC, 0x68, 0x26, 0x48, 0xAE, 0xAE, 0x56, 0xD8, 0x48, 0xAB, 0xE2, 0x69, 0x2F, 0x50, 0x4F, 0x70, 0xDE, 0x12, 0xC2, 0xB9, 0xE8, 0x65, 0xE4, 0x7D, 0xBE, 0x23},
                    },
                    MinimalRequiredRootSignaturesCountInCertificate = 2,
                    MaxCertificateDuration = TimeSpan.FromDays(367)
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
                SendOrEcho = false,
                Name = "01E",
                RegistrationId_ed25519privateKey = new byte[] { 0xDA, 0x52, 0x8B, 0x37, 0x80, 0xC5, 0x29, 0x61, 0x44, 0x59, 0x8D, 0x52, 0x59, 0x56, 0x78, 0x25, 0x0F, 0x91, 0xC6, 0x60, 0x66, 0x57, 0xE5, 0x5F, 0x23, 0xB9, 0x87, 0xF1, 0xCB, 0xB2, 0x08, 0xB8 },
                RegistrationId = new RegistrationId(new byte[] { 0x6E, 0xE9, 0x92, 0xA2, 0xAB, 0xA9, 0x31, 0x79, 0x99, 0xEA, 0xF9, 0x1C, 0xA6, 0x43, 0x34, 0xF7, 0x00, 0x2E, 0xAE, 0x32, 0xF6, 0x29, 0x54, 0xF2, 0x58, 0x9F, 0xFE, 0x5A, 0x61, 0x15, 0x81, 0x12 }),
                UserId = new UserId()
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0x64, 0x32, 0x01, 0x7F, 0xDC, 0x19, 0xD6, 0xE3, 0x69, 0xE7, 0x56, 0x74, 0x04, 0x8C, 0xD6, 0x15, 0xAA, 0x4F, 0xEF, 0xC6, 0x7A, 0x24, 0xA9, 0x28, 0xC0, 0x05, 0x3B, 0x84, 0xDF, 0x87, 0xAD, 0x3A},
                        new byte[] { 0xA9, 0x83, 0xDE, 0x5B, 0x49, 0x89, 0x41, 0x96, 0xA5, 0x46, 0x6B, 0xAB, 0xBC, 0xC7, 0x49, 0x52, 0x36, 0x5B, 0xAD, 0xD8, 0x3C, 0x1A, 0x93, 0xB5, 0x15, 0x85, 0x7C, 0xEE, 0x8B, 0x18, 0x79, 0x6D},
                        new byte[] { 0x67, 0x8B, 0x2F, 0xF2, 0xF3, 0x4D, 0x38, 0xFA, 0x8B, 0x5B, 0xA6, 0xFF, 0xC5, 0xC2, 0x0D, 0x22, 0xF6, 0x69, 0xD8, 0x0C, 0xDE, 0xBF, 0x87, 0x35, 0x04, 0x9B, 0x21, 0x15, 0x75, 0xBE, 0xDF, 0x84},
                        },
                    MinimalRequiredRootSignaturesCountInCertificate = 2,
                    MaxCertificateDuration = TimeSpan.FromDays(367)
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
                SendOrEcho = true,
                Name = "02S",
                RegistrationId_ed25519privateKey = new byte[] { 0x45, 0xBB, 0x47, 0xDE, 0x6A, 0xDA, 0x22, 0x56, 0x23, 0xC7, 0x7F, 0x7F, 0xB2, 0x5F, 0xAE, 0x09, 0x43, 0x90, 0x79, 0x54, 0xBF, 0x3F, 0x7B, 0xD6, 0xB7, 0xF3, 0x6A, 0xAF, 0x15, 0xE7, 0xEC, 0x36 },
                RegistrationId = new RegistrationId(new byte[] { 0x4B, 0x9A, 0x46, 0x56, 0xB5, 0x6F, 0x7C, 0xA5, 0xCB, 0x1D, 0x7D, 0xB8, 0x8A, 0xE7, 0x31, 0x14, 0xFF, 0x21, 0x4C, 0x43, 0xEA, 0x57, 0x67, 0x0D, 0xF3, 0x5F, 0xE1, 0xFA, 0x59, 0x5A, 0x1F, 0x2F }),
                UserId = new UserId()
                {
                   RootPublicKeys = new List<byte[]> {
                        new byte[] { 0xB2, 0x2F, 0xF4, 0x43, 0xE1, 0x16, 0xF1, 0x80, 0x2C, 0x69, 0x00, 0x80, 0xB2, 0xB3, 0xD6, 0xC0, 0x40, 0x97, 0xE0, 0x48, 0x93, 0xAB, 0x13, 0xD9, 0xDD, 0x33, 0xB9, 0x5B, 0xDA, 0xCA, 0xAD, 0x96},
                        new byte[] { 0xC5, 0x8F, 0x61, 0x95, 0x1F, 0xAA, 0x67, 0x6E, 0x6E, 0x3C, 0x16, 0x5D, 0x3D, 0x14, 0xFB, 0xE3, 0x44, 0x3D, 0x2D, 0xD9, 0xD5, 0x30, 0x28, 0xCC, 0x79, 0xD6, 0x59, 0xE4, 0x51, 0x62, 0x9A, 0x02},
                        new byte[] { 0xE7, 0xAA, 0x37, 0x15, 0x66, 0xA1, 0x48, 0x4C, 0x9E, 0xEB, 0x01, 0x0E, 0x6B, 0xBB, 0x36, 0xC3, 0xA4, 0x50, 0xD6, 0xE2, 0x71, 0xD3, 0xF1, 0x83, 0x14, 0x78, 0x48, 0x0A, 0x68, 0x99, 0x8D, 0x1A},
                        },
                    MinimalRequiredRootSignaturesCountInCertificate = 2,
                    MaxCertificateDuration = TimeSpan.FromDays(367)
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                   ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0x46, 0x52, 0x9C, 0xCF, 0xEF, 0x6B, 0xD7, 0x2F, 0x9D, 0x1E, 0xAA, 0xA5, 0xA9, 0x13, 0xFC, 0x08, 0xB1, 0x40, 0xB6, 0x68, 0x4A, 0x11, 0x24, 0xA2, 0x40, 0x69, 0x68, 0x25, 0x57, 0x2D, 0xE2, 0x7C},
                        new byte[] { 0x88, 0x66, 0x56, 0x68, 0x7B, 0xCB, 0xA4, 0xFC, 0xD6, 0xA9, 0xAB, 0x07, 0x79, 0x8E, 0xBD, 0xB8, 0xB1, 0x0F, 0xE9, 0xFF, 0x22, 0x2D, 0xC9, 0x8D, 0xEA, 0xDA, 0x28, 0xCD, 0x9A, 0xB1, 0xA2, 0xC6},
                        new byte[] { 0x72, 0x7D, 0xE3, 0x28, 0xE3, 0xA9, 0x84, 0xD8, 0x29, 0xDE, 0xD5, 0x81, 0xBB, 0x22, 0xEB, 0x9D, 0xAC, 0x69, 0xE0, 0x38, 0x58, 0xE8, 0x73, 0xDA, 0x97, 0x29, 0xD5, 0x89, 0xFE, 0x23, 0x06, 0x97},
                        }
                }
            },
            new PredefinedUser {
                SendOrEcho = false,
                Name = "02E",
                RegistrationId_ed25519privateKey = new byte[] { 0x46, 0x20, 0x72, 0xB5, 0x23, 0x1A, 0x12, 0xCB, 0x73, 0x72, 0x84, 0x9D, 0x94, 0x5D, 0xEC, 0x42, 0xFD, 0x15, 0x19, 0x82, 0x9C, 0x2B, 0x9F, 0x9D, 0x42, 0x24, 0xB4, 0x92, 0xDD, 0x96, 0x06, 0x88 },
                RegistrationId = new RegistrationId(new byte[] { 0xB5, 0x03, 0xC1, 0xB0, 0xAC, 0x84, 0xC8, 0x35, 0xD3, 0x8E, 0x1B, 0x59, 0xA1, 0xCB, 0xFD, 0xD2, 0xE4, 0x00, 0xC5, 0x40, 0x5D, 0xDF, 0xC8, 0x12, 0xDE, 0x9B, 0x2E, 0x2A, 0x86, 0xD3, 0x9A, 0x7F }),
                UserId = new UserId()
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0xD0, 0x1D, 0x88, 0xC9, 0x15, 0xE2, 0x16, 0x96, 0xF5, 0x3E, 0xE4, 0xC2, 0x31, 0x69, 0x77, 0x44, 0x89, 0x45, 0x76, 0x78, 0xB7, 0x1C, 0x9D, 0x7B, 0x29, 0x2F, 0xCD, 0xD1, 0xD1, 0xCA, 0x37, 0xA2},
                        new byte[] { 0x1A, 0x8D, 0x57, 0x7B, 0x5E, 0xE8, 0xE2, 0x65, 0x48, 0xB7, 0x41, 0xEE, 0x33, 0x65, 0x76, 0xC6, 0x38, 0xDF, 0xA4, 0x7C, 0x94, 0xE3, 0x7F, 0xEA, 0x35, 0xC2, 0x54, 0xFD, 0xA9, 0xFE, 0x32, 0xCC},
                        new byte[] { 0x2F, 0x05, 0x79, 0xE1, 0xD7, 0xBC, 0xBC, 0xD7, 0x7D, 0xE5, 0x6A, 0x82, 0x0D, 0xDE, 0xCF, 0x9F, 0x53, 0x4C, 0x56, 0x17, 0x76, 0xCC, 0x31, 0xB5, 0xCD, 0xA2, 0x48, 0x16, 0xA4, 0x19, 0xEA, 0x07},
                    },
                    MinimalRequiredRootSignaturesCountInCertificate = 2,
                    MaxCertificateDuration = TimeSpan.FromDays(367)
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                   ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0x4E, 0xBE, 0xD4, 0xBB, 0xDA, 0x69, 0xDF, 0x70, 0x9E, 0x7B, 0x4A, 0x4E, 0x10, 0xB9, 0x07, 0xCF, 0xDE, 0xA0, 0x29, 0xD7, 0x09, 0x8E, 0x2F, 0x43, 0x61, 0x39, 0x9A, 0x68, 0x73, 0x03, 0x94, 0x91},
                        new byte[] { 0xA4, 0xBE, 0x04, 0x49, 0xAF, 0xA3, 0x93, 0xCD, 0x6E, 0x0B, 0xF3, 0x48, 0xDB, 0xFF, 0x91, 0x64, 0xE8, 0x7F, 0xFC, 0xD4, 0x91, 0xB7, 0x66, 0x70, 0xD0, 0xF1, 0x2A, 0xE4, 0x59, 0x8E, 0xF1, 0x8C},
                        new byte[] { 0x9C, 0x77, 0x73, 0x7E, 0xED, 0x02, 0xBC, 0x86, 0x70, 0xFB, 0x6C, 0x9A, 0x57, 0xE4, 0xEB, 0x89, 0xF8, 0x4C, 0xCB, 0xD4, 0x91, 0xE9, 0x50, 0x8A, 0xAB, 0x61, 0x53, 0x3F, 0x8A, 0xEC, 0x24, 0xDD},
                   }
                }
             },




             new PredefinedUser {
                SendOrEcho = true,
                Name = "03S",
                RegistrationId_ed25519privateKey = new byte[] { 0x7F, 0x82, 0xE8, 0xDA, 0xB2, 0x55, 0xB9, 0x23, 0xF1, 0xE3, 0xCD, 0x46, 0xBF, 0x2A, 0x36, 0x49, 0xAF, 0xC2, 0x8A, 0x09, 0xB9, 0xB9, 0x56, 0xEF, 0xCC, 0xEF, 0xC8, 0x91, 0xCD, 0xBF, 0x8E, 0xD1 },
                RegistrationId = new RegistrationId(new byte[] { 0x54, 0x80, 0x87, 0x48, 0xAF, 0xE2, 0xC3, 0x48, 0x60, 0x00, 0x82, 0xD4, 0x8E, 0x59, 0xCB, 0x6A, 0xEB, 0xE6, 0xE7, 0x7E, 0xCB, 0xD3, 0xD3, 0xB5, 0x14, 0x21, 0x24, 0x54, 0x0F, 0x79, 0x65, 0x4C }),
                UserId = new UserId()
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0x0D, 0x9B, 0xE6, 0xC5, 0x46, 0xF4, 0x40, 0x9D, 0x9C, 0xCE, 0x6A, 0xD5, 0x01, 0x93, 0x7D, 0x7E, 0xD0, 0x1B, 0xFE, 0x61, 0x71, 0x8E, 0xB7, 0xD5, 0x1C, 0x32, 0x73, 0x56, 0x4C, 0xF2, 0x3F, 0x9C},
                        new byte[] { 0xAC, 0x56, 0xE5, 0x0D, 0x78, 0x4A, 0xAE, 0xAE, 0xC1, 0x03, 0xB7, 0x90, 0x59, 0x71, 0x1A, 0x81, 0x97, 0xDE, 0x7C, 0x48, 0x20, 0x5D, 0x30, 0x30, 0x0F, 0xE2, 0xC0, 0x1B, 0x9C, 0x63, 0x2D, 0x7D},
                        new byte[] { 0x14, 0xAD, 0x86, 0x2C, 0xEB, 0xA6, 0x66, 0xA4, 0xF6, 0xFD, 0x60, 0xE5, 0x4E, 0x06, 0x39, 0x29, 0x20, 0xB1, 0xF2, 0xF0, 0x85, 0x26, 0x6F, 0xFB, 0x90, 0xA0, 0xC8, 0x39, 0x8D, 0x57, 0xCA, 0x82},
                    },
                    MinimalRequiredRootSignaturesCountInCertificate = 2,
                    MaxCertificateDuration = TimeSpan.FromDays(367)
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                   ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0xDA, 0xE5, 0xE7, 0x5F, 0x07, 0x8E, 0x15, 0xE4, 0xC9, 0x5D, 0x6C, 0xF1, 0x0A, 0x7B, 0x3D, 0xAE, 0x0F, 0xBC, 0x48, 0x61, 0xC2, 0x93, 0xB4, 0x07, 0x5C, 0xFD, 0x84, 0x3D, 0xA6, 0x19, 0xAF, 0x6C},
                        new byte[] { 0xA9, 0x4B, 0x6E, 0x83, 0xFF, 0x58, 0xB2, 0xB8, 0x8C, 0xC2, 0x21, 0x38, 0xFE, 0x62, 0x26, 0xB0, 0xD3, 0x5A, 0xAD, 0x5A, 0x74, 0x14, 0xF8, 0xCF, 0xBD, 0xC4, 0x87, 0xAE, 0xEE, 0x18, 0xA0, 0x61},
                        new byte[] { 0x7C, 0xDB, 0x85, 0xDC, 0x41, 0x71, 0xE5, 0x3B, 0xC8, 0x6C, 0x7F, 0xDC, 0x1D, 0x3D, 0x8E, 0x15, 0x2B, 0xC4, 0xC3, 0x60, 0x2E, 0xEF, 0xC0, 0xCC, 0x84, 0x20, 0x8F, 0xA3, 0x08, 0xA6, 0x60, 0x51},
                   }
                }
            },
            new PredefinedUser {
                SendOrEcho = false,
                Name = "03E",
                RegistrationId_ed25519privateKey = new byte[] { 0xDC, 0x9A, 0x1E, 0xD0, 0x44, 0x23, 0xCB, 0x3D, 0x6F, 0xB7, 0x0D, 0xD3, 0x6A, 0x2E, 0x70, 0x48, 0xEC, 0xCE, 0x59, 0xC7, 0xC8, 0xA0, 0xB1, 0x3A, 0xF0, 0xEF, 0x2C, 0x12, 0x12, 0xFE, 0x76, 0x71 },
                RegistrationId = new RegistrationId(new byte[] { 0x56, 0x14, 0xEF, 0xE0, 0x0E, 0x5E, 0x79, 0xDB, 0x46, 0x0B, 0x27, 0xBF, 0x8A, 0xDC, 0x4A, 0x4D, 0xB2, 0x96, 0x12, 0x03, 0xCA, 0x9F, 0x6A, 0x2D, 0xDC, 0x7C, 0xC3, 0x88, 0x17, 0x8B, 0xEB, 0x71 }),
                UserId = new UserId()
                {
                    RootPublicKeys = new List<byte[]> {
                        new byte[] { 0x27, 0x9E, 0xB5, 0x22, 0x3B, 0x30, 0x93, 0xCD, 0xCF, 0x62, 0x17, 0x19, 0x63, 0xBE, 0xCB, 0x2F, 0x8C, 0x39, 0xE5, 0x03, 0xAE, 0xBE, 0x13, 0xF6, 0x97, 0x03, 0x39, 0x1C, 0x7E, 0xBA, 0xFA, 0xB1},
                        new byte[] { 0x61, 0x7E, 0x2B, 0x10, 0xCE, 0x7A, 0x78, 0xFF, 0x6F, 0xAF, 0x4A, 0x9D, 0xFE, 0x35, 0x56, 0x00, 0x6D, 0x03, 0xA5, 0x8B, 0xA5, 0xAB, 0xFA, 0x2F, 0xD2, 0xEA, 0xAC, 0xAD, 0xEF, 0x42, 0x87, 0x0E},
                        new byte[] { 0xB2, 0xDB, 0xD4, 0xB9, 0x78, 0xB1, 0x81, 0x33, 0xF6, 0x1D, 0x0D, 0x00, 0x2C, 0x6C, 0x1B, 0x0D, 0x62, 0x35, 0xC8, 0xB1, 0x06, 0xEB, 0xE2, 0x42, 0x9C, 0xB4, 0xCF, 0x62, 0x28, 0x13, 0xFE, 0x6D},
                    },
                    MinimalRequiredRootSignaturesCountInCertificate = 2,
                    MaxCertificateDuration = TimeSpan.FromDays(367)
                },
                UserRootPrivateKeys = new UserRootPrivateKeys
                {
                   ed25519privateKeys = new List<byte[]> {
                        new byte[] { 0xE8, 0x36, 0x73, 0xA6, 0x8A, 0xB5, 0x54, 0x21, 0xD0, 0x00, 0x1E, 0x25, 0xEB, 0x56, 0x96, 0x3D, 0x5C, 0xB9, 0x69, 0x4C, 0xD4, 0xB0, 0xB3, 0x7E, 0x58, 0x10, 0x73, 0x8B, 0xDD, 0x4C, 0x23, 0x73},
                        new byte[] { 0x5C, 0x90, 0x38, 0x8E, 0x4F, 0x8C, 0xC0, 0xD7, 0x29, 0x6A, 0x62, 0xF7, 0xED, 0x95, 0x28, 0x66, 0x19, 0x0A, 0xE9, 0x38, 0x44, 0x11, 0x48, 0xBD, 0x69, 0x8F, 0xDC, 0x39, 0x94, 0x32, 0xA8, 0x75},
                        new byte[] { 0x72, 0x43, 0xDC, 0xA7, 0xC7, 0xEB, 0xB1, 0x72, 0x1B, 0x13, 0x9B, 0x33, 0x95, 0x81, 0x42, 0x5D, 0x68, 0x8C, 0xEB, 0x8E, 0xA3, 0xEB, 0xCE, 0xD3, 0xA4, 0xBB, 0x29, 0xDF, 0xD4, 0xBF, 0xEB, 0x98},
                   }
                }
            },
        };
        public PredefinedUser LocalUser { get; set; }
        public PredefinedUser RemoteUser { get; set; }

        public ICommand InitializeUser1Sender => new DelegateCommand(() =>
        {
            LocalUser = PredefinedUsers[0];
            RemoteUser = PredefinedUsers[1];
            Initialize.Execute(null);
        });
        public ICommand InitializeUser1EchoResponder => new DelegateCommand(() =>
        {
            LocalUser = PredefinedUsers[1];
            Initialize.Execute(null);
        });
        public ICommand InitializeUser2Sender => new DelegateCommand(() =>
        {
            LocalUser = PredefinedUsers[2];
            RemoteUser = PredefinedUsers[3];
            Initialize.Execute(null);
        });
        public ICommand InitializeUser2EchoResponder => new DelegateCommand(() =>
        {
            LocalUser = PredefinedUsers[3];
            Initialize.Execute(null);
        });
        public ICommand InitializeUser3Sender => new DelegateCommand(() =>
        {
            LocalUser = PredefinedUsers[4];
            RemoteUser = PredefinedUsers[5];
            Initialize.Execute(null);
        });
        public ICommand InitializeUser3EchoResponder => new DelegateCommand(() =>
        {
            LocalUser = PredefinedUsers[5];
            Initialize.Execute(null);
        });        
        public ICommand Initialize => new DelegateCommand(() =>
        {
            if (!Initialized)
            {
                try
                {
                    Initialized = true;
                    RaisePropertyChanged(() => Initialized);

                    _visionChannel.ClearModules();
                    this.VisionChannelSourceId = $"U{LocalUser.Name}{(LocalUser.SendOrEcho ? "S" : "E")}";
                    _visionChannel.RegisterVisibleModule(VisionChannelSourceId, "DrpTester5", this);

                    var userEngine = new DrpPeerEngine(new DrpPeerEngineConfiguration
                    {
                        InsecureRandomSeed = _insecureRandom.Next(),
                        VisionChannel = _visionChannel,
                        VisionChannelSourceId = VisionChannelSourceId,
                        SandboxModeOnly_NumberOfDimensions = NumberOfDimensions,
                        LocalPort = LocalUdpPortString.ToUShortNullable(),
                    });

                    //var user4DrpPeerConfiguration = LocalDrpPeerConfiguration.Create(userEngine.CryptoLibrary, NumberOfDimensions);
                    //UserRootPrivateKeys.CreateUserId(3, 2, TimeSpan.FromDays(367), userEngine.CryptoLibrary, out var userRootPrivateKeys4, out var userId4);

                    //var user5DrpPeerConfiguration = LocalDrpPeerConfiguration.Create(userEngine.CryptoLibrary, NumberOfDimensions);
                    //UserRootPrivateKeys.CreateUserId(3, 2, TimeSpan.FromDays(367), userEngine.CryptoLibrary, out var userRootPrivateKeys5, out var userId5);

                    var localDrpPeerConfiguration = LocalDrpPeerConfiguration.Create(userEngine.CryptoLibrary, NumberOfDimensions,
                        LocalUser.RegistrationId_ed25519privateKey, LocalUser.RegistrationId);

                    if (int.TryParse(MaxNeighborsCountString, out var maxNeighborsCount))
                    {
                        localDrpPeerConfiguration.AbsoluteMaxNumberOfNeighbors = maxNeighborsCount;
                        localDrpPeerConfiguration.MinDesiredNumberOfNeighbors = Math.Max(1, maxNeighborsCount - 4);
                        localDrpPeerConfiguration.SoftMaxNumberOfNeighbors = Math.Max(1, maxNeighborsCount - 2);
                    }

                    var epEndpoints = RemoteEpEndPoints.ToList();
                    localDrpPeerConfiguration.EntryPeerEndpoints = RemoteEpEndPoints;

                    _userApp = new DrpTesterPeerApp(userEngine, localDrpPeerConfiguration, LocalUser.UserRootPrivateKeys, LocalUser.UserId) { EchoMessages = LocalUser.SendOrEcho == false };
                    _visionChannel.RegisterVisibleModule(VisionChannelSourceId, "DrpTester5/userApp", _userApp);

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
                        _visionChannel.Emit(userEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registration is complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
                        var waitForNeighborsSw = Stopwatch.StartNew();

                    // wait until number of neighbors reaches minimum
                    userEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromMilliseconds(300), () =>
                        {
                            AfterEpRegistration_ContinueIfConnectedToEnoughNeighbors(waitForNeighborsSw);
                        }, "waiting for connection with neighbors 324155");
                    });
                }
                catch (Exception exc)
                {
                    OnException(exc);
                }
            }
        });
        void OnException(Exception exc)
        {
            _visionChannel.Emit("", DrpTesterVisionChannelModuleName, AttentionLevel.mediumPain, $"error: {exc}");
        }

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
            if (!LocalUser.SendOrEcho) return;

            // send msg (with autoRetry=true)   wait for completion
            var userCertificate1 = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_userApp.DrpPeerEngine.CryptoLibrary, _userApp.UserId,
                _userApp.UserRootPrivateKeys, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            var sentText = $"echoTest_#{_sentCount}_from_{LocalUser.Name}_to_{RemoteUser.Name}_{_insecureRandom.Next()}";
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
           // if (failedCount >= 10000000)
            {
              //  _visionChannel.EmitListOfPeers(_userApp.DrpPeerEngine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
              //            AttentionLevel.strongPain,
              //            $"disposing the test: {failedCount} messages failed");

             //   BeginDisposeOnFailure();
             //   return false;
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

        string IVisibleModule.Status => TestReport;

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
               
        public ICommand TestUPnPdec10 => new DelegateCommand(() =>
        {
            _userApp.DrpPeerEngine.TestUPnPdec10();
        });               
    }
}
