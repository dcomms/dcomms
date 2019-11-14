using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Dcomms
{
    public class ExecutionTimeStatsCollector
    {
        Func<DateTime> _getTimeNow;
        public ExecutionTimeStatsCollector(Func<DateTime> getTimeNow)
        {
            _getTimeNow = getTimeNow;
        }

        class PeakDelay
        {
            public string ActionVisibleId;
            public double PeakExecutionTimeMs;
            public DateTime MeasurementTime;
            public string Details { get; set; }
        }
        Dictionary<string, PeakDelay> _peakDelays = new Dictionary<string, PeakDelay>(); // locked
            
        /// <param name="actionVisibleId">must be a selected from finite set of constant values, otherwise it will cause a memory leak</param>
        public bool OnMeasuredExecutionTime(string actionVisibleId, double executionTimeMs, string details = null)
        {
            PeakDelay pd;
            lock (_peakDelays)
            {
                if (!_peakDelays.TryGetValue(actionVisibleId, out pd))
                {
                    pd = new PeakDelay {ActionVisibleId = actionVisibleId, PeakExecutionTimeMs = executionTimeMs };
                    _peakDelays.Add(actionVisibleId, pd);
                }
            }

            if (executionTimeMs > pd.PeakExecutionTimeMs)
            {
                pd.PeakExecutionTimeMs = executionTimeMs;
                pd.MeasurementTime = _getTimeNow();
                pd.Details = details;
                return true;
            }
            return false;
        }
     //   string _peakExecutionTimeActionId;
    //    double? _peakExecutionTimeMs;
        public string PeakExecutionTimeStats
        {
            get
            {
                var r = new StringBuilder();
                r.Append("peak delays:");
                lock (_peakDelays)
                {
                    var pds = _peakDelays.Values.OrderByDescending(x=>x.PeakExecutionTimeMs).Take(7).ToList();
                    foreach (var pd in pds)
                        r.Append($"\r\n{pd.ActionVisibleId}: {pd.PeakExecutionTimeMs}ms at {pd.MeasurementTime.ToString("HH:mm:ss.fff")} {pd.Details}");
                }
                return r.ToString();
            }
        }      
    }
    public class ExecutionTimeTracker: IDisposable
    {
        Stopwatch _sw;
        readonly ExecutionTimeStatsCollector _etsc;
        readonly string _actionVisibleId;
        readonly Action<string> _writeToLog;
        readonly Action<string> _writeToLogNewMaximum;
        public string Details;
        public ExecutionTimeTracker(ExecutionTimeStatsCollector etsc, string actionVisibleId, Action<string> writeToLog, Action<string> writeToLogNewMaximum)
        {
            _writeToLog = writeToLog;
            _writeToLogNewMaximum = writeToLogNewMaximum;
            _actionVisibleId = actionVisibleId;
            _etsc = etsc;
            _sw = Stopwatch.StartNew();

            _writeToLog?.Invoke($"started tracker {actionVisibleId}");
        }
        public void Dispose()
        {
            if (_sw != null)
            {
                _sw.Stop();
                var newMaximum = _etsc.OnMeasuredExecutionTime(_actionVisibleId, _sw.Elapsed.TotalMilliseconds, Details);
                var wtl = newMaximum ? _writeToLogNewMaximum : _writeToLog;
                wtl?.Invoke($"stopped tracker {_actionVisibleId}: {_sw.Elapsed.TotalMilliseconds}ms {Details}");
                _sw = null;
            }
        }
    }
}
