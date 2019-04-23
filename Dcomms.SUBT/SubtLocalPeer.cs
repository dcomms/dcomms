using Dcomms.P2PTP;
using Dcomms.P2PTP.Extensibility;
using Dcomms.SUBT.SUBTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.SUBT
{
    /// <summary>
    /// sends requests to all connected peers, all streams: "txBwAdjustment"
    /// 
    /// when receives txBwAdjustment: checks whether it is acceptable, responds, updates config of transmitter threads
    /// 
    /// contains transmitter threads; 
    /// handles received BW payload, measures loss/jitter/bandwidth/rtt, sends back measurements
    /// 
    ///  
    /// </summary>
    public class SubtLocalPeer : ILocalPeerExtension
    {
        internal readonly SubtLocalPeerConfiguration Configuration;
        public SubtLocalPeer(SubtLocalPeerConfiguration configuration, SubtLocalPeer instanceFromPreviousTestAfterPause = null)
        {
            if (configuration.SenderThreadsCount <= 0 || configuration.SenderThreadsCount > 32) throw new ArgumentException(nameof(configuration.SenderThreadsCount));
            Configuration = configuration;
            if (instanceFromPreviousTestAfterPause != null)
                MeasurementsHistory.CopyFrom(instanceFromPreviousTestAfterPause.MeasurementsHistory);
        }
        internal void HandleException(Exception exc)
        {
            LocalPeer.HandleException(this, exc);
        }
        internal void WriteToLog(string message)
        {
            LocalPeer.WriteToLog(this, message);
        }
      //  internal void SignalErrorToDeveloper(o)

        public string ExtensionId => ExtensionIdPrefixes.SUBT;
        public byte[] PayloadPacketHeader => PacketHeaders.SubtPayload;
        List<SubtSenderThread> _senderThreads = new List<SubtSenderThread>();
        internal SubtSenderThread SenderThreadForNewStream
        {
            get
            {
                return _senderThreads[LocalPeer.Random.Next(_senderThreads.Count)];
            }
        }
        bool _initialized = false;

        public void DestroyWithLocalPeer()
        {
            if (_initialized)
            {
                foreach (var senderThread in _senderThreads)
                    senderThread.Dispose();
                _senderThreads.Clear();
            }
            _initialized = false;
        }

        public IConnectedPeerExtension OnConnectedPeer(IConnectedPeer connectedPeer)
        {
            return new SubtConnectedPeer(this, connectedPeer);
        }

        public void OnTimer100msApprox() // manager thread
        {
            AdjustTargetTxBandwidth_100msApprox();
            MeasurementsHistory.MeasureIfNeeded(this);
        }
      
        #region SUBT symbiosis logic
        public IEnumerable<SubtConnectedPeer> ConnectedPeers
        {
            get
            {
                var connectedPeers = LocalPeer.ConnectedPeers;
                if (connectedPeers != null)
                foreach (var cp in connectedPeers)
                    if (cp.Extensions.TryGetValue(this, out var cpx))
                        yield return (SubtConnectedPeer)cpx;               
            }
        }
      
        /// <param name="currentDependentMeasuredValue">some measurement (M) that depends on the TX bandwidth (T), and dM/dT > 0</param>
        /// <param name="targetDependentMeasuredValue">target value for the dependent measurement (M)</param>
        void UpdateTxBandwidth_100msApprox(ref float currentTxBwMultiplier, float currentDependentMeasuredValue, float targetDependentMeasuredValue, float speedCoefficient = 1.0f)
        {

            var div = (targetDependentMeasuredValue + currentDependentMeasuredValue);
            if (div == 0) return;
            float a = (targetDependentMeasuredValue - currentDependentMeasuredValue) / div;
            a *= Configuration.Speed100ms * speedCoefficient;


            var maxA = Configuration.Speed100msLimit;
            if (a < -maxA) a = -maxA; else if (a > maxA) a = maxA;
            currentTxBwMultiplier *= 1.0f + a;
            if (currentTxBwMultiplier < 1.0f - maxA) currentTxBwMultiplier = 1.0f - maxA; else if (currentTxBwMultiplier > 1.0f + maxA) currentTxBwMultiplier = 1.0f + maxA;
        }
        static void LimitHigh(ref float value, float limit)
        {
            if (value > limit) value = limit;
        }

        /// <summary>
        /// implements micro-adjustments according to differential equations
        /// main fuzzy logic of the distributed system is here
        /// </summary>
        void AdjustTargetTxBandwidth_100msApprox()
        {
            if (LocalPeer.Configuration.RoleAsSharedPassive)
            {
                foreach (var cp in ConnectedPeers)                  
                    foreach (var s in cp.Streams)
                        s.TargetTxBandwidth = s.RecentRxBandwidth; // symmetric                 
                return;
            }

            var connectedPeers = ConnectedPeers.ToArray();
            var localPeerBandwidthTargetConfigured = (float?)Configuration.BandwidthTargetMbps * 1024 * 1024;

            int nStreams = 0;
            float targetTxBandwidthSum = 0;
            foreach (var cp in connectedPeers)
                foreach (var s in cp.Streams)
                    if (s.TargetTxBandwidth != 0 && s.StreamIsIdleCached != true)
                    {
                        nStreams++;
                        targetTxBandwidthSum += s.TargetTxBandwidth;
                    }

            var currentLocalPeerTargetTxBandwidth = targetTxBandwidthSum;
            var streamsTargetTxBandwidthAverage = nStreams != 0 ? (targetTxBandwidthSum / nStreams) : 0;

            foreach (var cp in connectedPeers)
            {
                var cpStreams = cp.Streams.ToArray();
                if (cpStreams.Length == 0) continue;

                foreach (var s in cpStreams)
                {                    
                    if (s.LatestRemoteStatus != null && s.StreamIsIdleCached == false)
                    {
                        var currentTxBwMultiplier = 1.0f;

                        if (s.LatestRemoteStatus.IwantToIncreaseBandwidthUntilHighPacketLoss) // for user: this flag is set from local configuration; for passiveShared: this flag is reflected passively
                        {
                            #region meet with max possible bandwidth (and acceptable packet loss)
                            var recentPacketLoss = Math.Max(s.LatestRemoteStatus.RecentRxPacketLoss, s.RecentPacketLoss); // max of losses in both directions between peers

                            if (float.IsNaN(recentPacketLoss) || recentPacketLoss < 0.01f)
                                currentTxBwMultiplier *= 1.4f; // quickly grow bw in the beginning of the test to reach max bandwidth faster
                            else
                            {
                                const float acceptableLoss = 0.03f;
                                UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, recentPacketLoss, acceptableLoss);
                            }
                            #endregion
                        }                           

                        if (LocalPeer.Configuration.RoleAsUser && localPeerBandwidthTargetConfigured.HasValue)
                        {
                            UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, currentLocalPeerTargetTxBandwidth, localPeerBandwidthTargetConfigured.Value, 4);

                            var recentPacketLoss = Math.Max(s.LatestRemoteStatus.RecentRxPacketLoss, s.RecentPacketLoss); // max of losses in both directions between peers
                            const float acceptableLoss = 0.03f;
                            if (recentPacketLoss > acceptableLoss)
                            {
                                UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, recentPacketLoss, acceptableLoss);
                            }
                        }

                        #region meet with average for all streams
                        if (streamsTargetTxBandwidthAverage > 0)
                        {
                            UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, s.TargetTxBandwidth, streamsTargetTxBandwidthAverage, localPeerBandwidthTargetConfigured.HasValue ? 1 : 2);
                        }
                        #endregion           
                                                    
                        #region meet with RX bandwidth (make symmetric)  
                        if (s.LatestRemoteStatus.IhavePassiveRole == false)
                            if (s.RecentRxBandwidth > 0.0f)
                                UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, s.TargetTxBandwidth, s.RecentRxBandwidth); 
                        #endregion

                        if (s.TargetTxBandwidth > SubtLogicConfiguration.PerStreamSoftTxBandwidthLimit)
                        { // soft limit
                            UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, s.TargetTxBandwidth, SubtLogicConfiguration.PerStreamSoftTxBandwidthLimit);
                            WriteToLog($"stream {s} target TX bandwidth reached soft limit");
                        }

                        if (s.TargetTxBandwidth > SubtLogicConfiguration.PerStreamHardTxBandwidthLimit && currentTxBwMultiplier > 1)
                        { // hard limit
                            currentTxBwMultiplier = 1;
                            WriteToLog($"stream {s} target TX bandwidth reached hard limit");
                        }

                        s.TargetTxBandwidth *= currentTxBwMultiplier;   // 1024 * 200
                        s.TargetTxBandwidthLatestMultiplier = currentTxBwMultiplier;
                    }                   
                }
            }
        }
        #endregion


        public ILocalPeer LocalPeer { get; private set; }
        public void ReinitializeWithLocalPeer(ILocalPeer localPeer)
        {
            if (_initialized) throw new InvalidOperationException();
            DestroyWithLocalPeer();
            
            LocalPeer = localPeer;
            for (int i = 0; i < Configuration.SenderThreadsCount; i++)
                _senderThreads.Add(new SubtSenderThread(this, "subtSenderThread" + i));

            MeasurementsHistory.OnReinitialized(this);

            _initialized = true;
        }

        public SubtMeasurementsHistory MeasurementsHistory { get; private set; } = new SubtMeasurementsHistory();
    }

}
