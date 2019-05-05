using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dcomms.SUBT
{
    public class SubtMeasurementsHistory
    {
        LinkedList<SubtMeasurement> _measurements = new LinkedList<SubtMeasurement>(); // locked
        public IEnumerable<SubtMeasurement> Measurements
        {
            get
            {
                lock (_measurements)
                    return _measurements.ToList();
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
            foreach (var cp in subtLocalPeer.ConnectedPeers)
            {
                AverageSingle averageTxLoss = new AverageSingle();
                AverageSingle averageRxLoss = new AverageSingle();
                foreach (var s in cp.Streams)
                {
                    rxBandwidth += s.RecentRxBandwidth;
                    var st = s.LatestRemoteStatus;
                    if (st != null)
                    {
                        confirmedTxBandwidth += st.RecentRxBandwidth;
                        if (bestRttToPeers == null || s.RecentRtt < bestRttToPeers.Value)
                            bestRttToPeers = s.RecentRtt;
                        averageTxLoss.Input(st.RecentRxPacketLoss);
                    }
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


            return new SubtMeasurement
            {
                MeasurementPeriodEndUtc = subtLocalPeer.LocalPeer.DateTimeNowUtc,
                RxBandwidth = rxBandwidth,
                TxBandwidth = confirmedTxBandwidth,
                BestRttToPeers = bestRttToPeers,
                TxPacketLoss = averageTxLossG,
                RxPacketLoss = averageRxLossG
            };
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

                    lock (_measurements)
                    {
                        _measurements.AddLast(m);
                        if (_measurements.Count > SubtLogicConfiguration.MaxMeasurementsCount)
                            _measurements.RemoveFirst();
                    }

                    OnAddedNewMeasurement?.Invoke(m);
                }
            }
            catch (Exception exc)
            {
                subtLocalPeer.HandleException(exc);
            }
        }

        public event Action<SubtMeasurement> OnAddedNewMeasurement;
        public void Clear()
        {
            _measurements = new LinkedList<SubtMeasurement>();
        }
        internal void CopyFrom(SubtMeasurementsHistory previousInstanceBeforePause)
        {
            foreach (var m in previousInstanceBeforePause._measurements)
                _measurements.AddLast(m);
        }
    }
    public class SubtMeasurement
    {
        public string GroupString => $"{MeasurementPeriodEndUtc.Hour}:{MeasurementPeriodEndUtc.Minute}";

        public DateTime MeasurementPeriodEndUtc { get; set; }
        public DateTime MeasurementPeriodEnd => MeasurementPeriodEndUtc.ToLocalTime();
        public float RxBandwidth { get; set; } // download
        public string RxBandwidthString => RxBandwidth.BandwidthToString();
        public System.Drawing.Color RxBandwidthColor => RxBandwidth.BandwidthToColor();
        
        public float TxBandwidth { get; set; } // upload
        public System.Drawing.Color TxBandwidthColor => TxBandwidth.BandwidthToColor();
        public string TxBandwidthString => TxBandwidth.BandwidthToString();
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
    }
}
