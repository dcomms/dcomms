using Dcomms.SUBT;
using Dcomms;
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
        readonly MainViewModel _mainVM;
        public MainViewModel MainVM => _mainVM;
        public EasyGuiViewModel(MainViewModel mainVM)
        {
            if (Application.Current != null) Application.Current.Resources.Add("EasyGuiViewModel", this);
            _mainVM = mainVM;

            _timer = new DispatcherTimer(DispatcherPriority.SystemIdle);
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }
        internal void OnInitialized()
        {
            _mainVM.SubtLocalPeer.MeasurementsHistory.OnMeasured += MeasurementsHistory_OnMeasured;
        }
        bool _measurementsTabIsSelected = true;
        public bool MeasurementsTabIsSelected
        {
            get => _measurementsTabIsSelected;
            set
            {
                _measurementsTabIsSelected = value;
                RaisePropertyChanged(() => MeasurementsTabIsSelected);
            }
        }
        public bool UptimeStatisticsTabIsSelected { get; set; }

        private void MeasurementsHistory_OnMeasured(SubtMeasurement m) // manager thread
        {
            m.AppFlags0 = DowntimesTracker.IsItUptime(m) ? (byte)0x01 : (byte)0x00;

     //   public bool UpOrDown { get; set; }
      //  public string UpOrDownString => UpOrDown ? "up" : "down";
      //  public Color UpOrDownColor => UpOrDown ? Color.FromArgb(255, 150, 255, 150) : Color.FromArgb(255, 255, 150, 150);

           // if (_mainVM.EasyGuiTabIsSelected && MeasurementsTabIsSelected)
            {
                _mainVM.BeginInvokeInGuiThread(() =>
                {
                    RaisePropertyChanged(() => DisplayedMeasurements);
                    RaisePropertyChanged(() => MeasurementsCountInRamString);
                    //    OnAddedNewMeasurement?.Invoke(m);
                });
            }
        }
      //  public event Action<SubtMeasurement> OnAddedNewMeasurement;

        public void Dispose()
        {
            _timer.Stop();
        }
        
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
            try
            { 
                try
                {
                    var localPeer = _mainVM.SubtLocalPeer?.LocalPeer;
                    if (localPeer != null)
                    {
                        localPeer.InvokeInManagerThread(() =>
                        {
                            _latestMeasurement = _mainVM.SubtLocalPeer?.MeasurementsHistory.Measure(_mainVM.SubtLocalPeer);
                        });
                    }
                }
                catch (Exception exc)
                {
                    MainViewModel.HandleException(exc);
                }

                RaisePropertyChanged(() => RecentRxBandwidthString);
                RaisePropertyChanged(() => RecentTxBandwidthString);
                RaisePropertyChanged(() => RecentRttString);
                RaisePropertyChanged(() => MeasurementsVisibility);
                RaisePropertyChanged(() => StartVisibility);
                       
                _mainVM.DowntimesTracker.UpdateGui();
            }
            catch (Exception exc)
            {
                MainViewModel.HandleException(exc);
            }
        }

        SubtMeasurement _latestMeasurement;
        public Visibility MeasurementsVisibility => (_mainVM.Initialized || IsPaused) ? Visibility.Visible : Visibility.Collapsed;
        public string RecentRxBandwidthString => _latestMeasurement?.RxBandwidth.BandwidthToString();
        public string RecentTxBandwidthString => _latestMeasurement?.TxBandwidth.BandwidthToString();
      
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
            _mainVM.SubtLocalPeerConfigurationBandwidthTarget = MainViewModel.InitialBandwidthTarget;
            _mainVM.PredefinedReleaseMode.Execute(null);
            _mainVM.Initialize.Execute(null);
            RaisePropertyChanged(() => MeasurementsVisibility);
            RaisePropertyChanged(() => StartVisibility);
            RaisePropertyChanged(() => IsPaused);
            RaisePropertyChanged(() => DisplayMeasurementsMaxCount);            
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
            _mainVM.SubtLocalPeerConfigurationBandwidthTarget = MainViewModel.InitialBandwidthTarget;
            _mainVM.Initialize.Execute(null);
            IsPaused = false;
            RaisePropertyChanged(() => IsPaused);
        });

        public string MeasurementsCountInRamString
        {
            get
            {
                var r = _mainVM.SubtLocalPeer?.MeasurementsHistory?.MeasurementsCountInRam;
                if (r != null) return $"({r})";
                return "";
            }
        }
   
        public IEnumerable<SubtMeasurement> DisplayedMeasurements => _mainVM.SubtLocalPeer?.MeasurementsHistory?.DisplayedMeasurements;
        public int[] DisplayMeasurementsMaxCounts => new[] { 10, 20, 50, 100, 200, 500, 1000, 2000 };
        public int? DisplayMeasurementsMaxCount
        {
            get => _mainVM.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMaxCount;
            set
            {
                var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
                if (mh != null) mh.DisplayMeasurementsMaxCount = value ?? 10;
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }
        internal void GoToMeasurement(DateTime dt)
        {
            var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
            if (mh != null)
            {               
                mh.DisplayMeasurementsMostRecentDateTime = dt;

                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDate);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeH);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeM);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }

        public bool DisplayMeasurementsMostRecentDateHasValue
        {
            get
            {
                return (_mainVM.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime).HasValue;
            }
            set
            {
                var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;              
                if (mh != null)
                    mh.DisplayMeasurementsMostRecentDateTime = value ? (DateTime?)DateTime.Now : null;

                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDate);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeH);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeM);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }

        public DateTime? DisplayMeasurementsMostRecentDate
        {
            get
            {
                return _mainVM.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime;
            }
            set
            {
                if (!value.HasValue) throw new ArgumentNullException();
                var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
                if (mh != null)
                {
                    var newDateTime = value.Value;
                    newDateTime = newDateTime.AddHours(DisplayMeasurementsMostRecentTimeH);
                    newDateTime = newDateTime.AddMinutes(DisplayMeasurementsMostRecentTimeM);
                    mh.DisplayMeasurementsMostRecentDateTime = newDateTime;
                }
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDate);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }
        public IEnumerable<int> HoursList { get { for (int i = 0; i < 24; i++) yield return i; } }
        public int DisplayMeasurementsMostRecentTimeH
        {
            get
            {
                var dt = _mainVM.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime;
                return (dt ?? DateTime.Now).Hour;
            }
            set
            {
                var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
                if (mh != null && DisplayMeasurementsMostRecentDate != null)
                {
                    var newDateTime = DisplayMeasurementsMostRecentDate.Value.Date;
                    newDateTime = newDateTime.AddHours(value);
                    newDateTime = newDateTime.AddMinutes(DisplayMeasurementsMostRecentTimeM);
                    mh.DisplayMeasurementsMostRecentDateTime = newDateTime;
                }
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeH);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }
        public IEnumerable<int> MinutesList { get { for (int i = 0; i < 60; i++) yield return i; } }
        public int DisplayMeasurementsMostRecentTimeM
        {
            get
            {
                var dt = _mainVM.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime;
                return (dt ?? DateTime.Now).Minute;
            }
            set
            {
                var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
                if (mh != null && DisplayMeasurementsMostRecentDate != null)
                {
                    var newDateTime = DisplayMeasurementsMostRecentDate.Value.Date;
                    newDateTime = newDateTime.AddHours(DisplayMeasurementsMostRecentTimeH);
                    newDateTime = newDateTime.AddMinutes(value);
                    mh.DisplayMeasurementsMostRecentDateTime = newDateTime;
                }
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeM);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }


        public ICommand DisplayMeasurementsGotoEarlierMeasurements => new DelegateCommand(() =>
        {
            var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
            if (mh != null)
            {
                mh.DisplayMeasurementsMostRecentDateTime_GotoEarlierMeasurements();
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDate);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeH);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeM);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        });
        public ICommand DisplayMeasurementsGotoMostRecentMeasurements => new DelegateCommand(() =>
        {
            var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
            if (mh != null)
            {
                mh.DisplayMeasurementsMostRecentDateTime = null;
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        });
        public ICommand DisplayMeasurementsGotoLaterMeasurements => new DelegateCommand(() =>
        {
            var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
            if (mh != null)
            {
                mh.DisplayMeasurementsMostRecentDateTime_GotoLaterMeasurements();
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDate);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeH);
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeM);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        });
        public ICommand DisplayMeasurementsGotoPreviousDowntime => new DelegateCommand(() =>
        {
            var mh = _mainVM.SubtLocalPeer?.MeasurementsHistory;
            if (mh != null)
            {
                if (mh.GotoPreviousDisplayedMeasurement(m => (m.AppFlags0 & 0x01) == 0))
                {
                    RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                    RaisePropertyChanged(() => DisplayMeasurementsMostRecentDate);
                    RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeH);
                    RaisePropertyChanged(() => DisplayMeasurementsMostRecentTimeM);
                    RaisePropertyChanged(() => DisplayedMeasurements);
                }
                else
                    MessageBox.Show("Previous downtime is not dound");
            }
        });


        public ICommand ClearMeasurements => new DelegateCommand(() =>
        {
            _mainVM.SubtLocalPeer?.MeasurementsHistory?.Clear();
            RaisePropertyChanged(() => DisplayedMeasurements);
        });

        public ICommand ExportMeasurements => new DelegateCommand(() =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = "CSV files|*.csv";
            if (dlg.ShowDialog() == true)
            {
                var measurements = _mainVM.SubtLocalPeer?.MeasurementsHistory?.RamMeasurements;
                if (measurements == null || measurements.Count == 0)
                {
                    MessageBox.Show("No measurements");
                    return;
                }
                var sb = new StringBuilder();
                var delimiter = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
                sb.AppendFormat("Time{0}Download bandwidth (bps){0}Download bandwidth{0}Download packet loss (percent){0}Upload bandwidth (bps){0}Upload bandwidth{0}Upload packet loss{0}RTT (ping) (ms)\r\n", delimiter);
                foreach (var m in measurements)
                    sb.AppendFormat("{1}{0:yyyy-MM-DD HH:mm:ss}{2}{0}{3}{0}{4}{0}{5}{0}{6}{0}{7}{0}{8}\r\n", delimiter, 
                        m.MeasurementTime, m.RxBandwidth, m.RxBandwidthString, m.RxPacketLoss * 100, m.TxBandwidth, m.TxBandwidthString, m.TxPacketLoss * 100, m.BestRttToPeers?.TotalMilliseconds
                        );               
                sb.Append("The file is generated by StarTrinity Continuous Speed Test software. Write an email to support@startrinity.com in case of any problems");
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());               
            }
        });
    }
}
