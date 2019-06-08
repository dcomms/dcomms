using Dcomms.SUBT;
using Dcomms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Dcomms.SUBT.GUI
{
    public class EasyGuiViewModel : BaseNotify, IDisposable
    {
        readonly CstApp _cstApp;
        public CstApp CstApp => _cstApp;
        public EasyGuiViewModel(CstApp cstApp)
        {
            _cstApp = cstApp;
            _cstApp.User.AddStaticResource("EasyGuiViewModel", this);
            _cstApp.User.CreateIdleGuiTimer(TimeSpan.FromMilliseconds(100), Timer_Tick);
        }
        internal void OnInitialized()
        {
            _cstApp.SubtLocalPeer.MeasurementsHistory.OnMeasured += MeasurementsHistory_OnMeasured;
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

           // if (_cstApp.EasyGuiTabIsSelected && MeasurementsTabIsSelected)
            {
                _cstApp.BeginInvokeInGuiThread(() =>
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
        
        private void Timer_Tick()
        {
            try
            { 
                try
                {
                    var localPeer = _cstApp.SubtLocalPeer?.LocalPeer;
                    if (localPeer != null)
                    {
                        localPeer.InvokeInManagerThread(() =>
                        {
                            _latestMeasurement = _cstApp.SubtLocalPeer?.MeasurementsHistory.Measure(_cstApp.SubtLocalPeer);
                        });
                    }
                }
                catch (Exception exc)
                {
                    CstApp.HandleException(exc);
                }

                RaisePropertyChanged(() => RecentRxBandwidthString);
                RaisePropertyChanged(() => RecentTxBandwidthString);
                RaisePropertyChanged(() => RecentRttString);
                RaisePropertyChanged(() => MeasurementsVisible);
                RaisePropertyChanged(() => StartVisible);

                _cstApp.DowntimesTracker.UpdateGui();
            }
            catch (Exception exc)
            {
                CstApp.HandleException(exc);
            }
        }

        SubtMeasurement _latestMeasurement;
        public bool MeasurementsVisible => (_cstApp.Initialized || IsPaused);
        public string RecentRxBandwidthString => _latestMeasurement?.RxBandwidth.BandwidthToString();
        public string RecentTxBandwidthString => _latestMeasurement?.TxBandwidth.BandwidthToString();
      
        public string RecentRttString
        {
            get
            {
                return _latestMeasurement?.BestRttToPeersString;
            }
        }

        public bool StartVisible => (_cstApp.Initialized || IsPaused) == false;
        
        public ICommand StartTest => new DelegateCommand(() =>
        {
            _cstApp.SubtLocalPeerConfigurationBandwidthTarget = CstApp.InitialBandwidthTarget;
            _cstApp.PredefinedReleaseMode.Execute(null);
            _cstApp.Initialize.Execute(null);
            RaisePropertyChanged(() => MeasurementsVisible);
            RaisePropertyChanged(() => StartVisible);
            RaisePropertyChanged(() => IsPaused);
            RaisePropertyChanged(() => DisplayMeasurementsMaxCount);            
        });
       
        public bool IsPaused { get; set; }
        public ICommand PauseTest => new DelegateCommand(() =>
        {
            _cstApp.DeInitialize.Execute(null);
            IsPaused = true;
            RaisePropertyChanged(() => IsPaused);
        });
        public ICommand ResumeTest => new DelegateCommand(() =>
        {
            _cstApp.SubtLocalPeerConfigurationBandwidthTarget = CstApp.InitialBandwidthTarget;
            _cstApp.Initialize.Execute(null);
            IsPaused = false;
            RaisePropertyChanged(() => IsPaused);
        });

        public string MeasurementsCountInRamString
        {
            get
            {
                var r = _cstApp.SubtLocalPeer?.MeasurementsHistory?.MeasurementsCountInRam;
                if (r != null) return $"({r})";
                return "";
            }
        }
   
        public IEnumerable<SubtMeasurement> DisplayedMeasurements => _cstApp.SubtLocalPeer?.MeasurementsHistory?.DisplayedMeasurements;
        public int[] DisplayMeasurementsMaxCounts => new[] { 10, 20, 50, 100, 200, 500, 1000, 2000 };
        public int? DisplayMeasurementsMaxCount
        {
            get => _cstApp.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMaxCount;
            set
            {
                var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
                if (mh != null) mh.DisplayMeasurementsMaxCount = value ?? 10;
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        }
        public void GoToMeasurement(DateTime dt)
        {
            var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
                return (_cstApp.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime).HasValue;
            }
            set
            {
                var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;              
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
                return _cstApp.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime;
            }
            set
            {
                if (!value.HasValue) throw new ArgumentNullException();
                var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
                var dt = _cstApp.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime;
                return (dt ?? DateTime.Now).Hour;
            }
            set
            {
                var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
                var dt = _cstApp.SubtLocalPeer?.MeasurementsHistory?.DisplayMeasurementsMostRecentDateTime;
                return (dt ?? DateTime.Now).Minute;
            }
            set
            {
                var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
            var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
            var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
            if (mh != null)
            {
                mh.DisplayMeasurementsMostRecentDateTime = null;
                RaisePropertyChanged(() => DisplayMeasurementsMostRecentDateHasValue);
                RaisePropertyChanged(() => DisplayedMeasurements);
            }
        });
        public ICommand DisplayMeasurementsGotoLaterMeasurements => new DelegateCommand(() =>
        {
            var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
            var mh = _cstApp.SubtLocalPeer?.MeasurementsHistory;
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
                    _cstApp.User.ShowMessageToUser("Previous downtime is not found");
            }
        });
        
        public ICommand ClearMeasurements => new DelegateCommand(() =>
        {
            _cstApp.SubtLocalPeer?.MeasurementsHistory?.Clear();
            RaisePropertyChanged(() => DisplayedMeasurements);
            RaisePropertyChanged(() => MeasurementsCountInRamString);            
        });
        public ICommand ExportMeasurements => new DelegateCommand(() =>
        {
            if (_cstApp.User.ShowSaveFileDialog("csv", out var fileName))
            {
                var measurements = _cstApp.SubtLocalPeer?.MeasurementsHistory?.RamMeasurements;
                if (measurements == null || measurements.Count == 0)
                {
                    _cstApp.User.ShowMessageToUser("No measurements");
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
                System.IO.File.WriteAllText(fileName, sb.ToString());
            }           
        });

    }
}
