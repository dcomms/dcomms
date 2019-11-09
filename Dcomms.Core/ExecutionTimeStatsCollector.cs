using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Dcomms
{
    public class ExecutionTimeStatsCollector
    {
        class PeakDelay
        {
            public string ActionVisibleId;
            public double PeakExecutionTimeMs;
        }
        Dictionary<string, PeakDelay> _peakDelays = new Dictionary<string, PeakDelay>(); // locked
            
        /// <param name="actionVisibleId">must be a selected from finite set of constant values, otherwise it will cause a memory leak</param>
        public void OnMeasuredExecutionTime(string actionVisibleId, double executionTimeMs)
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
            }
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
                    var pds = _peakDelays.Values.OrderByDescending(x=>x.PeakExecutionTimeMs).Take(5).ToList();
                    foreach (var pd in pds)
                        r.Append($"\r\n{pd.ActionVisibleId}: {pd.PeakExecutionTimeMs}ms");
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
        public ExecutionTimeTracker(ExecutionTimeStatsCollector etsc, string actionVisibleId)
        {
            _actionVisibleId = actionVisibleId;
            _etsc = etsc;
            _sw = Stopwatch.StartNew();
        }
        public void Dispose()
        {
            if (_sw != null)
            {
                _sw.Stop();
                _etsc.OnMeasuredExecutionTime(_actionVisibleId, _sw.Elapsed.TotalMilliseconds);
                _sw = null;
            }
        }
    }
}
