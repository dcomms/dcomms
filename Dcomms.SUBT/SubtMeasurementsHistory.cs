﻿using System;
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
                        if (bestRttToPeers == null || s.RecentRtt < bestRttToPeers.Value)
                            bestRttToPeers = s.RecentRtt;

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


            return new SubtMeasurement
            {
                MeasurementTime = subtLocalPeer.LocalPeer.DateTimeNow,
                TargetBandwidth = subtLocalPeer.Configuration.BandwidthTarget,
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
    }
}
