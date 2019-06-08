using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dcomms.SUBT
{
    /// <summary>
    /// stores SUBT measurements
    /// </summary>
    public class SubtMeasurementsHistory
    {
        LinkedList<SubtMeasurement> _measurementsInRam = new LinkedList<SubtMeasurement>(); // locked // newest first
        public int MeasurementsCountInRam { get { lock (_measurementsInRam) return _measurementsInRam.Count; } }

        public DateTime? DisplayMeasurementsMostRecentDateTime { get; set; } // null if = now
        public int DisplayMeasurementsMaxCount { get; set; } = 10; // = page size
        public IEnumerable<SubtMeasurement> DisplayedMeasurements // newest first
        {
            get
            {
                lock (_measurementsInRam)
                {
                    int returnedCount = 0;
                    foreach (var m in _measurementsInRam)
                    {
                        if (DisplayMeasurementsMostRecentDateTime != null && m.MeasurementTime > DisplayMeasurementsMostRecentDateTime.Value)
                            continue;

                        yield return m;
                        returnedCount++;
                        if (returnedCount >= DisplayMeasurementsMaxCount)
                            break;
                    }
                }
            }
        }
        public void DisplayMeasurementsMostRecentDateTime_GotoEarlierMeasurements()
        {
            var oldestDisplayedM = DisplayedMeasurements.LastOrDefault();
            if (oldestDisplayedM != null)
            {
                DisplayMeasurementsMostRecentDateTime = oldestDisplayedM.MeasurementTime;
            }
        }
        public void DisplayMeasurementsMostRecentDateTime_GotoLaterMeasurements()
        {
            if (DisplayMeasurementsMostRecentDateTime == null) return;
            lock (_measurementsInRam)
            {
                // enumerate measurements starting from tail (oldest)
                // note when MeasurementTime is GREATER than DisplayMeasurementsMostRecentDateTime
                // count, until DisplayMeasurementsMaxCount
                // set new value to DisplayMeasurementsMostRecentDateTime

                int counter = 0;
                var item = _measurementsInRam.Last;
                for (; ; )
                {
                    if (item == null) break;
                    if (item.Value.MeasurementTime >= DisplayMeasurementsMostRecentDateTime.Value)
                    {
                        DisplayMeasurementsMostRecentDateTime = item.Value.MeasurementTime;
                        counter++;
                        if (counter >= DisplayMeasurementsMaxCount)
                        {
                            break;
                        }
                    }
                    item = item.Previous;
                }
            }
        }
        public bool GotoPreviousDisplayedMeasurement(Func<SubtMeasurement,bool> gotoThisMeasurement)
        {
            lock (_measurementsInRam)
            {
                foreach (var m in _measurementsInRam)
                {
                    if (DisplayMeasurementsMostRecentDateTime != null && m.MeasurementTime >= DisplayMeasurementsMostRecentDateTime.Value)
                        continue;

                    if (gotoThisMeasurement(m) == true)
                    {
                        DisplayMeasurementsMostRecentDateTime = m.MeasurementTime;
                        return true;
                    }
                }
            }
            return false;
        }
        public List<SubtMeasurement> RamMeasurements
        {
            get
            {
                lock (_measurementsInRam)
                {
                    return _measurementsInRam.ToList();
                }
            }
        }


        public SubtMeasurement Measure(SubtLocalPeer subtLocalPeer) // manager thread // used by easyGui
        {
            // measure
            float rxBandwidth = 0;
            float confirmedTxBandwidth = 0;
            TimeSpan? bestRttToPeers = null;
            float? averageTxLossG = null;
            float? averageRxLossG = null;
            foreach (var connectedPeer in subtLocalPeer.ConnectedPeers)
            {
                AverageSingle averageTxLoss = new AverageSingle();
                AverageSingle averageRxLoss = new AverageSingle();
                foreach (var s in connectedPeer.Streams)
                {
                    rxBandwidth += s.RecentRxBandwidth;
                    var st = s.LatestRemoteStatus;
                    if (st != null)
                    {
                        confirmedTxBandwidth += st.RecentRxBandwidth;
                        var rtt = s.RecentRttConsideringP2ptp;
                        if (bestRttToPeers == null || rtt < bestRttToPeers.Value)
                            bestRttToPeers = rtt;

                        if (st.RecentRxBandwidth > SubtLogicConfiguration.MinBandwidthPerStreamForPacketLossMeasurement)
                            averageTxLoss.Input(st.RecentRxPacketLoss);
                    }
                    if (s.RecentRxBandwidth > SubtLogicConfiguration.MinBandwidthPerStreamForPacketLossMeasurement)
                        averageRxLoss.Input(s.RecentPacketLoss);
                }
                var averageRxLossAverage = averageRxLoss.Average;
                if (averageRxLossAverage.HasValue)
                {
                    if (averageRxLossG == null || averageRxLossAverage.Value < averageRxLossG.Value)
                        averageRxLossG = averageRxLossAverage;
                }
                var averageTxLossAverage = averageTxLoss.Average;
                if (averageTxLossAverage.HasValue)
                {
                    if (averageTxLossG == null || averageTxLossAverage.Value < averageTxLossG.Value)
                        averageTxLossG = averageTxLossAverage;
                }
            }
            
            var r = new SubtMeasurement
            {
                MeasurementTime = subtLocalPeer.LocalPeer.DateTimeNow,
                TargetBandwidth = subtLocalPeer.Configuration.BandwidthTarget,
                RxBandwidth = rxBandwidth,
                TxBandwidth = confirmedTxBandwidth,
                BestRttToPeers = bestRttToPeers,
                TxPacketLoss = averageTxLossG,
                RxPacketLoss = averageRxLossG
            };
            return r;
        }
      
        internal void OnReinitialized(SubtLocalPeer subtLocalPeer)
        {
            _initializedTime = subtLocalPeer.LocalPeer.DateTimeNowUtc;
        }

        DateTime? _initializedTime;
        DateTime? _lastTimeMeasured;
        internal void MeasureIfNeeded(SubtLocalPeer subtLocalPeer) // manager thread
        {
            try
            {
                var now = subtLocalPeer.LocalPeer.DateTimeNowUtc;
                if (_initializedTime == null) return;
                if (now < _initializedTime.Value.AddTicks(SubtLogicConfiguration.MeasurementInitializationTimeTicks)) return;
                               
                if (_lastTimeMeasured == null || _lastTimeMeasured.Value.AddTicks(SubtLogicConfiguration.MeasurementsIntervalTicks) < now)
                {
                    _lastTimeMeasured = now;

                    // measure
                    var m = Measure(subtLocalPeer);

                    lock (_measurementsInRam)
                    {
                        _measurementsInRam.AddFirst(m);
                        if (_measurementsInRam.Count > SubtLogicConfiguration.MaxMeasurementsCountInRAM)
                            _measurementsInRam.RemoveLast();
                    }

                    OnMeasured?.Invoke(m);
                }
            }
            catch (Exception exc)
            {
                subtLocalPeer.HandleException(exc);
            }
        }

        public event Action<SubtMeasurement> OnMeasured;
        public void Clear()
        {
            _measurementsInRam = new LinkedList<SubtMeasurement>();
        }
        internal void CopyFrom(SubtMeasurementsHistory previousInstanceBeforePause)
        {
            lock (previousInstanceBeforePause._measurementsInRam)
                foreach (var m in previousInstanceBeforePause._measurementsInRam)
                    _measurementsInRam.AddLast(m);
        }
    }
    public class SubtMeasurement
    {
        public DateTime MeasurementTime { get; set; } // measurements from the past are passed via IIR filter      
        public float TargetBandwidth { get; set; }
        
        public float RxBandwidth { get; set; } // download
        public string RxBandwidthString => RxBandwidth.BandwidthToString(TargetBandwidth);
        public System.Drawing.Color RxBandwidthColor => RxBandwidth.BandwidthToColor(TargetBandwidth);
        
        public float TxBandwidth { get; set; } // upload
        public System.Drawing.Color TxBandwidthColor => TxBandwidth.BandwidthToColor(TargetBandwidth);
        public string TxBandwidthString => TxBandwidth.BandwidthToString(TargetBandwidth);
        public TimeSpan? BestRttToPeers { get; set; }
        public System.Drawing.Color BestRttToPeersColor => BestRttToPeers.RttToColor();
        public string BestRttToPeersString => MiscProcedures.TimeSpanToString(BestRttToPeers);

        public float? RxPacketLoss { get; set; } // 0..1
        public string RxPacketLossString => RxPacketLoss.HasValue ? String.Format("{0:0.00}%", RxPacketLoss * 100) : "";
        public System.Drawing.Color RxPacketLossColor => RxPacketLoss.PacketLossToColor();
        public System.Drawing.Color RxPacketLossColor_UBw => RxPacketLoss.PacketLossToColor_UBw();
        

        public float? TxPacketLoss { get; set; } // 0..1
        public string TxPacketLossString => TxPacketLoss.HasValue ? String.Format("{0:0.00}%", TxPacketLoss * 100) : "";
        public System.Drawing.Color TxPacketLossColor => TxPacketLoss.PacketLossToColor();
        public System.Drawing.Color TxPacketLossColor_UBw => TxPacketLoss.PacketLossToColor_UBw();

        public byte AppFlags0; // CST: 0x01 = up/down

        public string CstUpDownString => ((AppFlags0 & 0x01) != 0) ? "up" : "down";
        public System.Drawing.Color CstUpDownColor => ((AppFlags0 & 0x01) != 0) ? System.Drawing.Color.FromArgb(255, 150, 255, 150) : System.Drawing.Color.FromArgb(255, 255, 150, 150);
    }
}
