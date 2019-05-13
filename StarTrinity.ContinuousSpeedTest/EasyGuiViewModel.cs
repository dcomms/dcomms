using Dcomms.SUBT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace StarTrinity.ContinuousSpeedTest
{
    public class EasyGuiViewModel : BaseNotify, IDisposable
    {
        public Visibility UserModeVisibility => _mainVM.LocalPeerConfigurationRoleAsUser ? Visibility.Visible : Visibility.Collapsed;
        readonly MainViewModel _mainVM;
        public EasyGuiViewModel(MainViewModel mainVM)
        {
            _mainVM = mainVM;

            _timer = new DispatcherTimer(DispatcherPriority.SystemIdle);
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }
        internal void OnInitialized()
        {
            _mainVM.SubtLocalPeer.MeasurementsHistory.OnAddedNewMeasurement += MeasurementsHistory_OnAddedNewMeasurement;
        }

        private void MeasurementsHistory_OnAddedNewMeasurement(SubtMeasurement m) // manager thread
        {
            if (_mainVM.EasyGuiTabIsSelected)
            {
                Dispatcher.CurrentDispatcher.Invoke(() =>
                {
                    RaisePropertyChanged(() => Measurements);
                    OnAddedNewMeasurement?.Invoke(m);
                });
            }
        }
        public event Action<SubtMeasurement> OnAddedNewMeasurement;

        public void Dispose()
        {
            _timer.Stop();
        }

        public string BandwidthTargetString => _mainVM.SubtLocalPeerConfiguration.BandwidthTargetString;

        //public IEnumerable<EasyGuiTestMode> Modes => ModesS;
        //static readonly EasyGuiTestMode[] ModesS = new[]
        //    {
        //        new EasyGuiTestMode { Description = "Max. bandwidth", BandwidthTargetMbps = null},
        //        new EasyGuiTestMode { Description = "Light monitoring, 50kbps", BandwidthTargetMbps = 50.0 / 1024 },
        //        new EasyGuiTestMode { Description = "VoIP, 1 concurrent call", BandwidthTargetMbps = 87.2 / 1024 },
        //        new EasyGuiTestMode { Description = "VoIP, 10 concurrent calls", BandwidthTargetMbps = 10 * 87.2 / 1024 },
        //        new EasyGuiTestMode { Description = "VoIP, 100 concurrent calls", BandwidthTargetMbps = 100 * 87.2 / 1024 },
        //        new EasyGuiTestMode { Description = "Video, 1080p HD", BandwidthTargetMbps = 6},
        //        new EasyGuiTestMode { Description = "Video, 720p HD", BandwidthTargetMbps = 3},
        //    };
       

        DispatcherTimer _timer;
        private void Timer_Tick(object sender, EventArgs e)
        {
            var subtLocalPeer = _mainVM.SubtLocalPeer;
            if (subtLocalPeer != null)
            {
                subtLocalPeer.LocalPeer.InvokeInManagerThread(() =>
                {
                    _latestMeasurement = subtLocalPeer.MeasurementsHistory.Measure(_mainVM.SubtLocalPeer);
                });
            }

            RaisePropertyChanged(() => RecentRxBandwidthString);
            RaisePropertyChanged(() => RecentTxBandwidthString);
            RaisePropertyChanged(() => RecentRttString);
            RaisePropertyChanged(() => MeasurementsVisibility);
            RaisePropertyChanged(() => StartVisibility);
        }

        SubtMeasurement _latestMeasurement;

        public Visibility MeasurementsVisibility => (_mainVM.Initialized || IsPaused) ? Visibility.Visible : Visibility.Collapsed;
        public string RecentRxBandwidthString
        {
            get
            {
                return _latestMeasurement?.RxBandwidthString;
            }
        }
        public string RecentTxBandwidthString
        {
            get
            {
                return _latestMeasurement?.TxBandwidthString;
            }
        }
        public string RecentRttString
        {
            get
            {
                return _latestMeasurement?.BestRttToPeersString;
            }
        }

        public Visibility StartVisibility => (_mainVM.Initialized || IsPaused) ? Visibility.Collapsed : Visibility.Visible;
        public ICommand StartTest => new DelegateCommand(() =>
        {
            _mainVM.SubtLocalPeerConfigurationBandwidthTargetMbps = 50.0 / 1024;
            _mainVM.Initialize.Execute(null);
            RaisePropertyChanged(() => MeasurementsVisibility);
            RaisePropertyChanged(() => StartVisibility);
            RaisePropertyChanged(() => IsPaused);
        });
       
        public bool IsPaused { get; set; }
        public ICommand PauseTest => new DelegateCommand(() =>
        {
            _mainVM.DeInitialize.Execute(null);
            IsPaused = true;
            RaisePropertyChanged(() => IsPaused);
        });
        public ICommand ResumeTest => new DelegateCommand(() =>
        {
            _mainVM.SubtLocalPeerConfigurationBandwidthTargetMbps = 50.0 / 1024;
            _mainVM.Initialize.Execute(null);
            IsPaused = false;
            RaisePropertyChanged(() => IsPaused);
        });

        public IEnumerable<SubtMeasurement> Measurements => _mainVM.SubtLocalPeer?.MeasurementsHistory?.Measurements;

        public ICommand ClearMeasurements => new DelegateCommand(() =>
        {
            _mainVM.SubtLocalPeer?.MeasurementsHistory?.Clear();
            RaisePropertyChanged(() => Measurements);
        });

        public ICommand ExportMeasurements => new DelegateCommand(() =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = "CSV files|*.csv";
            if (dlg.ShowDialog() == true)
            {
                
                var sb = new StringBuilder();
                var delimiter = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
                sb.AppendFormat("Time{0}Download bandwidth (bps){0}Download bandwidth{0}Upload bandwidth (bps){0}Upload bandwidth{0}RTT (ping) (ms)\r\n", delimiter);
                foreach (var m in Measurements.ToList())
                {
                    sb.AppendFormat("{1}{0:yyyy-MM-DD HH:mm:ss}{2}{0}{3}{0}{4}{0}{5}{0}{6}\r\n", delimiter, m.MeasurementPeriodEnd, m.RxBandwidth, m.RxBandwidthString, m.TxBandwidth, m.TxBandwidthString, m.BestRttToPeers?.TotalMilliseconds);
                }
                sb.Append("The file is generated with StarTrinity Continuous Speed Test software. Write an email to support@startrinity.com in case of any problems");
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());               
            }
        });
    }

}
