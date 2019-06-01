using Dcomms.SUBT;
using Dcomms;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Drawing;

namespace Dcomms.SUBT.GUI
{
    public class DowntimesTracker : BaseNotify
    {
        readonly CstApp _cstApp;
        public DowntimesTracker(CstApp cstApp)
        {
            _cstApp = cstApp;
            Clear.Execute(null);
        }

        public ICommand Clear => new DelegateCommand(() =>
        {           
            Fragments.Clear();
            _currentFragment = null;
            UpdateGui();           
        });

        internal void UpdateGui() // 100ms
        {
            RaisePropertyChanged(() => TabHeaderString);
            if (_cstApp.LocalPeer != null)
            {
                if (_currentFragment != null)
                    _currentFragment.SetStopTime(_cstApp.LocalPeer.DateTimeNow);              
                RaisePropertyChanged(() => UptimeDurationString);
                RaisePropertyChanged(() => DowntimeDurationString);
            }
        }

        UpDownTimeFragment _currentFragment = null; // accessed by manager thread

        public static bool IsItUptime(SubtMeasurement m)
        {
            if (m.RxBandwidth < m.TargetBandwidth * 0.1f) return false;
            if (m.TxBandwidth < m.TargetBandwidth * 0.1f) return false;
            if (m.BestRttToPeers > TimeSpan.FromMilliseconds(2000)) return false;
            if (m.RxPacketLoss > 0.5) return false;
            if (m.TxPacketLoss > 0.5) return false;            
            return true;
        }

        public string UptimeDurationString
        {
            get
            {
                GetDurations(out var uptimeDuration, out var downtimeDuration, out var numberOfDowntimes);
                if (uptimeDuration.Ticks == 0) return "";
                return String.Format("{0} ({1:0.0000}%)", uptimeDuration.TimeSpanToStringHMS(), 100.0 * uptimeDuration.Ticks / (uptimeDuration.Ticks + downtimeDuration.Ticks));
            }
        }
        public string TabHeaderString
        {
            get
            {
                GetDurations(out var uptimeDuration, out var downtimeDuration, out var numberOfDowntimes);
                if (uptimeDuration.Ticks == 0) return "";
                return String.Format("{0:0.00}%({1})", 100.0 * uptimeDuration.Ticks / (uptimeDuration.Ticks + downtimeDuration.Ticks), numberOfDowntimes);
            }
        }
        public string DowntimeDurationString
        {
            get
            {
                GetDurations(out var uptimeDuration, out var downtimeDuration, out var numberOfDowntimes);
                if (uptimeDuration.Ticks == 0) return "";
                return String.Format("{0} ({1:0.0000}%). {2} downtime(s)", downtimeDuration.TimeSpanToStringHMS(), 100.0 * downtimeDuration.Ticks / (uptimeDuration.Ticks + downtimeDuration.Ticks), numberOfDowntimes);
            }
        }
        public ObservableCollection<UpDownTimeFragment> Fragments { get; private set; } = new ObservableCollection<UpDownTimeFragment>(); // newest first
      
        void GetDurations(out TimeSpan uptimeDuration, out TimeSpan downtimeDuration, out int numberOfDowntimes) // gui thead
        {
            uptimeDuration = TimeSpan.Zero;
            downtimeDuration = TimeSpan.Zero;
            numberOfDowntimes = 0;
           
            foreach (var f in Fragments)
            {
                var duration = (f.StopTime - f.StartTime);
                if (f.UpOrDown)
                    uptimeDuration += duration;
                else
                {
                    downtimeDuration += duration;
                    numberOfDowntimes++;
                }
            }  
        }

        internal void MeasurementsHistory_OnMeasured(SubtMeasurement m) // manager thread
        {
            _cstApp.BeginInvokeInGuiThread(() =>
            {
                if (_currentFragment == null)
                {
                    if (IsItUptime(m))
                    {
                        _currentFragment = new UpDownTimeFragment
                        {
                            StartTime = m.MeasurementTime,
                            StopTime = m.MeasurementTime,
                            UpOrDown = true,
                        };
                        Fragments.Insert(0, _currentFragment);
                    }
                }
                else
                {
                    var up = IsItUptime(m);
                    if (_currentFragment.UpOrDown ^ up)
                    {
                        _currentFragment.SetStopTime(m.MeasurementTime); 
                        _currentFragment = new UpDownTimeFragment
                        {
                            StartTime = m.MeasurementTime,
                            StopTime = m.MeasurementTime,
                            UpOrDown = up,
                        };
                        Fragments.Insert(0, _currentFragment);
                    }
                }
            });
        }
    }
    public class UpDownTimeFragment: BaseNotify
    {
        public override string ToString() => $"{UpOrDownString} [{StartTime}-{StopTime}] ({DurationString})";

        public DateTime StartTime { get; set; }
        public DateTime StopTime { get; set; } // is nulllable only for current fragment

        internal void SetStopTime(DateTime stopTime)
        {
            StopTime = stopTime;
            RaisePropertyChanged(() => StopTime);
            RaisePropertyChanged(() => StartTime);
            RaisePropertyChanged(() => DurationString);
            RaisePropertyChanged(() => DurationColor);
        }

        TimeSpan Duration => StopTime - StartTime;
        public string DurationString => Duration.TimeSpanToStringHMS();
        public Color DurationColor => UpOrDown ? MiscProcedures.UptimeDurationToColor(Duration) : MiscProcedures.DowntimeDurationToColor(Duration);
        public bool UpOrDown { get; set; }
        public string UpOrDownString => UpOrDown ? "up" : "down";
        public Color UpOrDownColor => UpOrDown ? MiscProcedures.UptimeDurationToColor(Duration) : MiscProcedures.DowntimeDurationToColor(Duration);

    }
}
