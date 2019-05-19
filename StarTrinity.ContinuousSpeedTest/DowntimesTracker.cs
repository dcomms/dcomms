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

namespace StarTrinity.ContinuousSpeedTest
{
    public class DowntimesTracker : BaseNotify
    {
        readonly MainViewModel _mainVM;
        public DowntimesTracker(MainViewModel mainVM)
        {
            _mainVM = mainVM;
        }

        public ICommand Clear => new DelegateCommand(() =>
        {
            _currentFragment = null;
            _fragments = new LinkedList<UpDownTimeFragment>(); // locked
        });

        DateTime? _lastTimeUpdatedFragmentsGui;
        internal void UpdateGui()
        {
            RaisePropertyChanged(() => TabHeaderString);

            if (_mainVM.EasyGuiViewModel.UptimeStatisticsTabIsSelected)
            {
                if (_mainVM.LocalPeer != null)
                {
                    RaisePropertyChanged(() => UptimeDurationString);
                    RaisePropertyChanged(() => DowntimeDurationString);

                    var dt = _mainVM.LocalPeer.DateTimeNowUtc;

                    if (_lastTimeUpdatedFragmentsGui == null || dt > _lastTimeUpdatedFragmentsGui.Value.AddSeconds(2))
                    {
                        _lastTimeUpdatedFragmentsGui = dt;
                        _mainVM.InvokeInGuiThread(() => { RaisePropertyChanged(() => Fragments); });
                    }
                }
            }
        }

        UpDownTimeFragment _currentFragment = null; // accessed by manager thread

        static bool IsItUptime(SubtMeasurement m)
        {
            if (m.RxBandwidth < m.TargetBandwidth * 0.1f) return false;
            if (m.TxBandwidth < m.TargetBandwidth * 0.1f) return false;
            if (m.BestRttToPeers > TimeSpan.FromMilliseconds(2000)) return false;
            if (m.RxPacketLoss > 0.05) return false;
            if (m.TxPacketLoss > 0.05) return false;            
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
        LinkedList<UpDownTimeFragment> _fragments = new LinkedList<UpDownTimeFragment>(); // locked
        public List<UpDownTimeFragment> Fragments
        {
            get
            {
                lock (_fragments)
                { 
                    var r = _fragments.ToList();
                    if (_currentFragment != null)
                    {
                        _currentFragment.StopTime = _mainVM.LocalPeer.DateTimeNow;
                        r.Add(_currentFragment);
                    }
                    return r;
                }
            }
        }
        void GetDurations(out TimeSpan uptimeDuration, out TimeSpan downtimeDuration, out int numberOfDowntimes)
        {
            uptimeDuration = TimeSpan.Zero;
            downtimeDuration = TimeSpan.Zero;
            numberOfDowntimes = 0;
            lock (_fragments)
            {
                foreach (var f in Fragments)
                {
                    var duration = (f.StopTime.Value - f.StartTime);
                    if (f.UpOrDown)
                        uptimeDuration += duration;
                    else
                    {
                        downtimeDuration += duration;
                        numberOfDowntimes++;
                    }
                }              
            }
        }

        internal void MeasurementsHistory_OnMeasured(SubtMeasurement m) // manager thread
        {
            if (_currentFragment == null)
            {
                if (IsItUptime(m))
                {
                    _currentFragment = new UpDownTimeFragment
                    {
                        StartTime = m.MeasurementTime,
                        UpOrDown = true,
                    };
                }
            }
            else
            {
                var up = IsItUptime(m);
                if (_currentFragment.UpOrDown ^ up)
                {
                    _currentFragment.StopTime = m.MeasurementTime;
                    lock (_fragments)
                        _fragments.AddLast(_currentFragment);
                    
                    _currentFragment = new UpDownTimeFragment
                    {
                        StartTime = m.MeasurementTime,
                        UpOrDown = up,
                    };
                }
            }
        }
    }
    public class UpDownTimeFragment
    {
        public DateTime StartTime { get; set; }
        public DateTime? StopTime { get; set; } // is null only for current fragment
        TimeSpan Duration => ((StopTime ?? DateTime.Now) - StartTime);
        public string DurationString => Duration.TimeSpanToStringHMS();
        public Color DurationColor => UpOrDown ? MiscProcedures.UptimeDurationToColor(Duration) : MiscProcedures.DowntimeDurationToColor(Duration);
        public bool UpOrDown { get; set; }
        public string UpOrDownString => UpOrDown ? "up" : "down";
        public Color UpOrDownColor => UpOrDown ? Color.FromArgb(255, 150, 255, 150) : Color.FromArgb(255, 255, 150, 150);
    }
}
