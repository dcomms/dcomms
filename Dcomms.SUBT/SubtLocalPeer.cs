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
        public readonly SubtLocalPeerConfiguration Configuration;
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
            AdjustStreamsTargetTxBandwidth_100msApprox();
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
      
        ///// <param name="currentDependentMeasuredValue">some measurement (M) that depends on the TX bandwidth (T), and dM/dT > 0</param>
        ///// <param name="targetDependentMeasuredValue">target value for the dependent measurement (M)</param>
        //void UpdateTxBandwidth_100msApprox(ref float currentTxBwMultiplier, float currentDependentMeasuredValue, float targetDependentMeasuredValue, float speedCoefficient = 1.0f)
        //{

        //    var div = (targetDependentMeasuredValue + currentDependentMeasuredValue);
        //    if (div == 0) return;
        //    float a = (targetDependentMeasuredValue - currentDependentMeasuredValue) / div;
        //    a *= Configuration.Speed100ms * speedCoefficient;


        //    var maxA = Configuration.Speed100msLimit;
        //    if (a < -maxA) a = -maxA; else if (a > maxA) a = maxA;
        //    currentTxBwMultiplier *= 1.0f + a;
        //    if (currentTxBwMultiplier < 1.0f - maxA) currentTxBwMultiplier = 1.0f - maxA; else if (currentTxBwMultiplier > 1.0f + maxA) currentTxBwMultiplier = 1.0f + maxA;
        //}
        static void LimitHigh(ref float value, float limit)
        {
            if (value > limit) value = limit;
        }
        static float LimitSubtRemoteStatusPacketRemoteBandwidth(float remoteTargetBandwidth)
        {
            LimitHigh(ref remoteTargetBandwidth, SubtLogicConfiguration.MaxLocalTxBandwidthPerStream);
            return remoteTargetBandwidth;
        }
        //static bool StreamIsGoodForSubt(SubtConnectedPeerStream s, long now64)
        //{
        //    if (s.StreamIsIdleCached == true) return false;
        //    if (s.LatestRemoteStatus == null) return false;

        //    if (s.SubtConnectedPeer.Type != ConnectedPeerType.toConfiguredServer)
        //    {
        //        if (s.GetLifetime(now64) < SubtLogicConfiguration.MinimumUser2UserStreamLifetimeBeforeUsage)
        //            return false;
        //    }
        //    return true;
        //}
  

        /// <summary>
        /// implements micro-adjustments according to differential equations
        /// main fuzzy logic of the distributed system is here
        /// </summary>
        void AdjustStreamsTargetTxBandwidth_100msApprox()
        {
            /*
             if this is passive peer: passively reflect bw  within limits (TargetTxBandwidth = remote RecentTxBandwidth)  (mirror bandwidth)
             
             if this is user:
               * concepts:
                 * peer.targetBW   * userPeer.remote status target BW
                 * sum of stream.targetBW - must match to peer.targetBW 
                 * passive peers and user peers, user peers with good health
                 * p2p signals: targetBW, actualTxBW, actualRxBW, txBwAdjustment
             until target bandwidth matches:
             - distribute between current users, according to previous remote state of user peer
             - rest of BW distribute between passive peers

            adjustment procedure:
            - if quality is good for user2user connection for a long time, and if connection has good quality for a long time (p2ptp level? rtt and loss): send signal adjustmentUp
              - if both users meet with signals - gradually increase bw and decrease BW to passive peers             
            - if quality becomes bad  or one of peers sends signal adjustmentDown: gradually decrease BW (and increase used BW of passive peers)
             
            periodic procedures:
               * send local targetBw and signals and rx status to peer
               * adjust passive peers' targetBW to match to user-set targetBW
               * having measured quality of user2user connection: send adjustment signals
               * having local and remote adjustment signal: adjust user2user BW
             */

            if (LocalPeer.Configuration.RoleAsSharedPassive)
            {
                float totalAssignedTxBandwidth = 0;
                foreach (var cp in ConnectedPeers)
                    foreach (var s in cp.Streams)
                        if (s.LatestRemoteStatus?.IhavePassiveRole == false)
                        {
                            s.TargetTxBandwidth = LimitSubtRemoteStatusPacketRemoteBandwidth(s.LatestRemoteStatus?.RecentTxBandwidth ?? 0);
                            totalAssignedTxBandwidth += s.TargetTxBandwidth;
                            if (totalAssignedTxBandwidth > SubtLogicConfiguration.MaxLocalTxBandwidthPerPeer)
                                s.TargetTxBandwidth = 0; // limit max total BW                                               
                        }
                return;
            }
            
            var passiveConnectedPeers = (from cp in ConnectedPeers
                                  orderby cp.Type == ConnectedPeerType.toConfiguredServer ? 0 : 1
                                  select new
                                  {
                                      cp,
                                      streams = cp.Streams.Where(x => x.LatestRemoteStatus?.IhavePassiveRole == true)//.OrderBy(x => x.StreamId.Id)//.Where(x => StreamIsGoodForSubt(x, now64))
                                        .ToArray()
                                  }
                ).Where(cp => cp.streams.Length != 0).ToArray();

            #region stateless bandwidth distribution      
            var targetTxBandwidthRemaining = Configuration.BandwidthTarget;
            LimitHigh(ref targetTxBandwidthRemaining, SubtLogicConfiguration.MaxLocalTxBandwidthPerPeer);
            int numberOfStreams = 0;

            // initial distribution of SubtLogicConfiguration.PerStreamMinRecommendedBandwidth per peer
            foreach (var cp in passiveConnectedPeers)
            {
                numberOfStreams += cp.streams.Length;
                var s = cp.streams[0];
                
                var bw = Math.Min(targetTxBandwidthRemaining, SubtLogicConfiguration.PerStreamMinRecommendedBandwidth);
                s.TargetTxBandwidth = bw;
                targetTxBandwidthRemaining -= bw;
                if (targetTxBandwidthRemaining <= 0) break;
            }

            // initial distribution of SubtLogicConfiguration.PerStreamMinRecommendedBandwidth per extra streams
            if (targetTxBandwidthRemaining > 0)
            {
                foreach (var cp in passiveConnectedPeers)
                    for (int si = 1; si < cp.streams.Length; si++)
                    {
                        var s = cp.streams[si];
                        var bw = Math.Min(targetTxBandwidthRemaining, SubtLogicConfiguration.PerStreamMinRecommendedBandwidth);
                        s.TargetTxBandwidth = bw;
                        targetTxBandwidthRemaining -= bw;
                        if (targetTxBandwidthRemaining <= 0) break;
                    }
            }

            // distribute remaining bandwidth between all streams
            if (targetTxBandwidthRemaining > 0)
            {
                var statelessTargetTxBandwidthRemainingPerStream = targetTxBandwidthRemaining / numberOfStreams;
                foreach (var cp in passiveConnectedPeers)
                    foreach (var s in cp.streams)
                    {
                        s.TargetTxBandwidth += statelessTargetTxBandwidthRemainingPerStream;
                    }
            }
            #endregion




            /*
             * old code
            float totalAssignedTxBandwidth = 0;
            foreach (var cp in ConnectedPeers)
                foreach (var s in cp.Streams)
                    s.TargetTxBandwidth0 = 0;
            
            if (LocalPeer.Configuration.RoleAsSharedPassive)
            {
                foreach (var cp in ConnectedPeers)
                    foreach (var s in cp.Streams)
                        if (s.LatestRemoteStatus?.IhavePassiveRole == false)                       
                        {
                            s.TargetTxBandwidth0 = LimitSubtRemoteStatusPacketRemoteBandwidth(s.LatestRemoteStatus?.StatelessTargetTxBandwidth ?? 0);
                            var bw = LimitSubtRemoteStatusPacketRemoteBandwidth(s.LatestRemoteStatus?.TargetTxBandwidth ?? 0);
                            totalAssignedTxBandwidth += bw;
                            if (totalAssignedTxBandwidth > SubtLogicConfiguration.MaxLocalTxBandwidthPerPeer)
                            {
                                s.TargetTxBandwidth = 0; // limit max total BW
                                s.StatelessTargetTxBandwidth = 0;
                            }
                            else
                                s.TargetTxBandwidth = bw;
                        }                   
                return;
            }

            var now64 = LocalPeer.Time64;
            var connectedPeers = (from cp in ConnectedPeers
                                  orderby cp.Type == ConnectedPeerType.toConfiguredServer ? 0 : 1
                                  select new 
                                  {
                                      cp,
                                      streams = cp.Streams.OrderBy(x => x.StreamId.Id).Where(x => StreamIsGoodForSubt(x, now64)).ToArray()
                                  } 
                ).Where(cp => cp.streams.Length != 0).ToArray();

            #region stateless bandwidth distribution      
            var statelessTargetTxBandwidthRemaining = Configuration.BandwidthTarget;
            LimitHigh(ref statelessTargetTxBandwidthRemaining, SubtLogicConfiguration.MaxLocalTxBandwidthPerPeer);
            int numberOfStreams = 0;

            // initial distribution of SubtLogicConfiguration.PerStreamMinRecommendedBandwidth per peer
            foreach (var cp in connectedPeers)
            {
                numberOfStreams += cp.streams.Length;
                var s = cp.streams[0];
                

                var bw = Math.Min(statelessTargetTxBandwidthRemaining, SubtLogicConfiguration.PerStreamMinRecommendedBandwidth);
                s.StatelessTargetTxBandwidth =  bw;
                statelessTargetTxBandwidthRemaining -= bw;
                if (statelessTargetTxBandwidthRemaining <= 0) break;
            }

            // initial distribution of SubtLogicConfiguration.PerStreamMinRecommendedBandwidth per extra streams
            if (statelessTargetTxBandwidthRemaining > 0)
            {
                foreach (var cp in connectedPeers)
                    for (int si = 1; si < cp.streams.Length; si++)
                    {
                        var s = cp.streams[si];
                        var bw = Math.Min(statelessTargetTxBandwidthRemaining, SubtLogicConfiguration.PerStreamMinRecommendedBandwidth);
                        s.StatelessTargetTxBandwidth = bw;
                        statelessTargetTxBandwidthRemaining -= bw;
                        if (statelessTargetTxBandwidthRemaining <= 0) break;
                    }
            }

            // distribute remaining bandwidth between all streams
            if (statelessTargetTxBandwidthRemaining > 0)
            {
                var statelessTargetTxBandwidthRemainingPerStream = statelessTargetTxBandwidthRemaining / numberOfStreams;
                foreach (var cp in connectedPeers)
                    foreach (var s in cp.streams)
                    {
                        s.StatelessTargetTxBandwidth += statelessTargetTxBandwidthRemainingPerStream;
                    }
            }
            #endregion

            foreach (var cp in connectedPeers)
                foreach (var s in cp.streams)
                    AdjustTargetTxBandwidth_100msApprox(s);       
                    */
        }

        ///// <summary>
        ///// main procedure with differential equation
        ///// </summary>
        //void AdjustTargetTxBandwidth_100msApprox(SubtConnectedPeerStream s)
        //{
        //    var remoteStatus = s.LatestRemoteStatus;
        //    if (remoteStatus != null)
        //    {

        //        // inputs: 
        //        //remoteStatus.StatelessTargetTxBandwidth
        //        //remoteStatus.RecentRxBandwidth
        //        //s.TargetTxBandwidth
        //        //s.StatelessTargetTxBandwidth

        //        // output:
        //       // s.TargetTxBandwidth


        //        //xxxxxxxxxxxxxx
        //       // var currentTxBwMultiplier = 1.0f;

        //        //if (s.LatestRemoteStatus.IwantToIncreaseBandwidthUntilHighPacketLoss) // for user: this flag is set from local configuration; for passiveShared: this flag is reflected passively
        //        //{
        //        //    #region meet with max possible bandwidth (and acceptable packet loss)
        //        //    var recentPacketLoss = Math.Max(s.LatestRemoteStatus.RecentRxPacketLoss, s.RecentPacketLoss); // max of losses in both directions between peers

        //        //    if (float.IsNaN(recentPacketLoss) || recentPacketLoss < 0.01f)
        //        //        currentTxBwMultiplier *= 1.4f; // quickly grow bw in the beginning of the test to reach max bandwidth faster
        //        //    else
        //        //    {
        //        //        const float acceptableLoss = 0.03f;
        //        //        UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, recentPacketLoss, acceptableLoss);
        //        //    }
        //        //    #endregion
        //        //}                           

        //        if (LocalPeer.Configuration.RoleAsUser)
        //        {
        //            UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, currentLocalPeerTargetTxBandwidth, localPeerBandwidthTargetConfigured, 4);

        //            var recentPacketLoss = Math.Max(s.LatestRemoteStatus.RecentRxPacketLoss, s.RecentPacketLoss); // max of losses in both directions between peers
        //            const float acceptableLoss = 0.03f;
        //            if (recentPacketLoss > acceptableLoss)
        //            {
        //                UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, recentPacketLoss, acceptableLoss);
        //            }
        //        }

        //        //#region meet with average for all streams
        //        //if (streamsTargetTxBandwidthAverage > 0)
        //        //{
        //        //    UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, s.TargetTxBandwidth, streamsTargetTxBandwidthAverage, 
        //        //        1);
        //        //}
        //        //#endregion           

        //        #region meet with RX bandwidth (make symmetric)  
        //        if (s.LatestRemoteStatus.IhavePassiveRole == false)
        //            if (s.RecentRxBandwidth > 0.0f)
        //                UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, s.TargetTxBandwidth, s.RecentRxBandwidth);
        //        #endregion

        //        if (s.TargetTxBandwidth > SubtLogicConfiguration.PerStreamSoftTxBandwidthLimit)
        //        { // soft limit
        //            UpdateTxBandwidth_100msApprox(ref currentTxBwMultiplier, s.TargetTxBandwidth, SubtLogicConfiguration.PerStreamSoftTxBandwidthLimit);
        //            WriteToLog($"stream {s} target TX bandwidth reached soft limit");
        //        }

        //        if (s.TargetTxBandwidth > SubtLogicConfiguration.PerStreamHardTxBandwidthLimit && currentTxBwMultiplier > 1)
        //        { // hard limit
        //            currentTxBwMultiplier = 1;
        //            WriteToLog($"stream {s} target TX bandwidth reached hard limit");
        //        }

        //        s.TargetTxBandwidth *= currentTxBwMultiplier;   // 1024 * 200
        //        s.TargetTxBandwidthLatestMultiplier = currentTxBwMultiplier;


        //    }
        //}
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
