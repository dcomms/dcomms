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

        public ICommand IncreaseNumberOfEngines => new DelegateCommand(() =>
        {
            AddEngine();
            NumberOfEngines = (NumberOfEngines ?? 0) + 1;
            RaisePropertyChanged(() => NumberOfEngines);

        });

        public int NumberOfLocalPeersToRegisterPerEngine { get; set; } = 1;
        int? _numberOfNeighborsToKeep;
        public string NumberOfNeighborsToKeep
        {
            get => $"{_numberOfNeighborsToKeep}";
            set
            {
                _numberOfNeighborsToKeep = String.IsNullOrEmpty(value) ? (int?)null : int.Parse(value);
                foreach (var a in _apps)
                    a.LocalDrpPeer.Configuration.NumberOfNeighborsToKeep = _numberOfNeighborsToKeep;
                RaisePropertyChanged(() => NumberOfNeighborsToKeep);
            }
        }

        public ICommand IncreaseNumberOfNeighborsToKeep => new DelegateCommand(() =>
        {
            NumberOfNeighborsToKeep = ((_numberOfNeighborsToKeep ?? 0) + 1).ToString();
        });

        public bool Initialized { get; private set; }

        readonly Random _insecureRandom = new Random();
        readonly List<DrpTesterPeerApp> _apps = new List<DrpTesterPeerApp>();
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

            if (NumberOfEngines == null)
            {
                NumberOfEngines = 1;
                RaisePropertyChanged(() => NumberOfEngines);
            }

           
            NumberOfLocalPeersToRegisterPerEngine = 1;
            RaisePropertyChanged(() => NumberOfLocalPeersToRegisterPerEngine);
            

            Initialize.Execute(null);
        });
        

        public ICommand Initialize => new DelegateCommand(() =>
        {
            if (Initialized) throw new InvalidOperationException();

            for (int engineIndex = 0; engineIndex < NumberOfEngines; engineIndex++)
            {
                AddEngine();
            }

            Initialized = true;
            RaisePropertyChanged(() => Initialized);
        });
        void AddEngine()
        {
            var engine = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                InsecureRandomSeed = _insecureRandom.Next(),
                LocalPort = (ushort?)(LocalPortNullable + _engines.Count),
                VisionChannel = _visionChannel,
                VisionChannelSourceId = $"E{_engines.Count}",
                SandboxModeOnly_DisableRecentUniquePow1Data = true
            });
            _engines.Add(engine);
            for (int localPeerIndex = 0; localPeerIndex < NumberOfLocalPeersToRegisterPerEngine; localPeerIndex++)
            {
                var localDrpPeerConfiguration = LocalDrpPeerConfiguration.CreateWithNewKeypair(engine.CryptoLibrary); 
                localDrpPeerConfiguration.NumberOfNeighborsToKeep = _numberOfNeighborsToKeep;
                localDrpPeerConfiguration.EntryPeerEndpoints = EpEndPoints;
                   
                var app = new DrpTesterPeerApp(engine, localDrpPeerConfiguration);

                if (EpEndPoints.Length != 0)
                { // connect to remote EPs
                       
                    var sw = Stopwatch.StartNew();
                    _visionChannel.Emit(engine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName,
                        AttentionLevel.guiActivity, $"registering...");
                    engine.BeginRegister(localDrpPeerConfiguration, app, (localDrpPeer) =>
                    {
                        app.LocalDrpPeer = localDrpPeer;
                        _visionChannel.Emit(engine.Configuration.VisionChannelSourceId, DrpTesterVisionChannelModuleName, AttentionLevel.guiActivity, $"registration complete in {(int)sw.Elapsed.TotalMilliseconds}ms");
                    });
                }
                else
                { // EP host mode                   
                    engine.BeginCreateLocalPeer(localDrpPeerConfiguration, app, (localDrpPeer) =>
                    {
                        app.LocalDrpPeer = localDrpPeer;
                    });
                }
                _apps.Add(app);
            }

        }

        public void Dispose()
        {
            foreach (var e in _engines)
               e.Dispose();
        }
    }
}
