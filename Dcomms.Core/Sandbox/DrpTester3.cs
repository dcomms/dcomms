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
        const string DrpTesterVisionChannelModuleName = "drpTester3";
        public ushort? LocalPortNullable { get; set; }
        
        IPEndPoint[] EpEndPoints = new IPEndPoint[0];
        public string EpEndPointsString
        {
            get
            {
                if (EpEndPoints == null) return "";
                return String.Join(";", EpEndPoints.Select(x => x.ToString()));
            }
            set
            {
                if (String.IsNullOrEmpty(value)) EpEndPoints = null;
                else EpEndPoints = (from valueStr in value.Split(';')
                                     let pos = valueStr.IndexOf(':')
                                     where pos != -1
                                     select new IPEndPoint(
                                         IPAddress.Parse(valueStr.Substring(0, pos)),
                                         int.Parse(valueStr.Substring(pos + 1))
                                         )
                        ).ToArray();
            }
        }

        public int? NumberOfEngines  { get; set; }
        public int NumberOfLocalPeersToRegisterPerEngine { get; set; } = 1;
        public int? NumberOfNeighborsToKeep { get; set; } 
        public bool Initialized { get; private set; }

        readonly Random _insecureRandom = new Random();
        readonly List<DrpPeerEngine> _engines = new List<DrpPeerEngine>();

        readonly VisionChannel _visionChannel;
        public DrpTester3(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }

        public ICommand InitializeEpHost => new DelegateCommand(() =>
        {
            LocalPortNullable = 12000;
            RaisePropertyChanged(() => LocalPortNullable);

            NumberOfEngines = 1;
            RaisePropertyChanged(() => NumberOfEngines);
            NumberOfLocalPeersToRegisterPerEngine = 1;
            RaisePropertyChanged(() => NumberOfLocalPeersToRegisterPerEngine);

            Initialize.Execute(null);
        });
        public ICommand InitializeUser => new DelegateCommand(() =>
        {
            EpEndPoints = new[] { new IPEndPoint(IPAddress.Parse("195.154.173.208"), 12000) };
            RaisePropertyChanged(() => EpEndPointsString);


            NumberOfEngines = 1;
            RaisePropertyChanged(() => NumberOfEngines);

            NumberOfLocalPeersToRegisterPerEngine = 1;
            RaisePropertyChanged(() => NumberOfLocalPeersToRegisterPerEngine);

           // NumberOfNeighborsToKeep = 2;

            Initialize.Execute(null);
        });
        

        public ICommand Initialize => new DelegateCommand(() =>
        {
            if (Initialized) throw new InvalidOperationException();

            for (int engineIndex = 0; engineIndex < NumberOfEngines; engineIndex++)
            {
                var engine = new DrpPeerEngine(new DrpPeerEngineConfiguration
                {
                    InsecureRandomSeed = _insecureRandom.Next(),
                    LocalPort = (ushort?)(LocalPortNullable + engineIndex),
                    VisionChannel = _visionChannel,
                    VisionChannelSourceId = $"E{engineIndex}",
                    SandboxModeOnly_DisableRecentUniquePow1Data = true
                });
                for (int localPeerIndex = 0; localPeerIndex < NumberOfLocalPeersToRegisterPerEngine; localPeerIndex++)
                {
                    var localDrpPeerConfiguration = LocalDrpPeerConfiguration.CreateWithNewKeypair(engine.CryptoLibrary); 
                    localDrpPeerConfiguration.NumberOfNeighborsToKeep = NumberOfNeighborsToKeep;
                    localDrpPeerConfiguration.EntryPeerEndpoints = EpEndPoints;
                   
                    var app = new DrpTesterPeerApp(engine, localDrpPeerConfiguration);

                    if (EpEndPoints.Length != 0)
                    { // connect to remote EPs
                       
                        var sw = Stopwatch.StartNew();
                        _visionChannel.Emit(engine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                            AttentionLevel.guiActivity, $"registering...");
                        engine.BeginRegister(localDrpPeerConfiguration, app, (localDrpPeer) =>
                        {
                            _visionChannel.Emit(engine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
                        });
                    }
                    else
                    { // EP host mode                   
                        engine.BeginCreateLocalPeer(localDrpPeerConfiguration, app, (localDrpPeer) =>
                        {                       
                        });
                    }
                }

                _engines.Add(engine);
            }

            Initialized = true;
            RaisePropertyChanged(() => Initialized);
        });

        public void Dispose()
        {
            foreach (var engine in _engines)
                engine.Dispose();
        }
    }
}
