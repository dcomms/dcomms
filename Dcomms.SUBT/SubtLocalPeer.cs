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
            var measurement = MeasurementsHistory.MeasureIfNeeded(this);
            if (measurement != null)
            {
                LatestMeasurement = measurement;
                IsHealthyForU2uSymbiosis_Update(measurement);
            }
            AdjustStreamsTargetTxBandwidth_InitiateAdjustmentRequestsIfNeeded_100msApprox();
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
        public IEnumerable<SubtConnectedPeer> ConnectedPeersForGui => ConnectedPeers.OrderByDescending(x => x.TargetTxBandwidth).ThenByDescending(x => x.RemoteLibraryVersion);
     
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
       

        /// <summary>
        /// implements micro-adjustments according to differential equations
        /// main fuzzy logic of the distributed system is here
        /// </summary>
        void AdjustStreamsTargetTxBandwidth_InitiateAdjustmentRequestsIfNeeded_100msApprox()
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
                            if (totalAssignedTxBandwidth > Configuration.MaxLocalTxBandwidth)
                                s.TargetTxBandwidth = 0; // limit max total BW           // todo pain signal here                                    
                        }
                        else
                        {
                            s.TargetTxBandwidth = 0;
                        }
                return;
            }
            

            var targetTxBandwidthRemaining = Configuration.BandwidthTarget;
            LimitHigh(ref targetTxBandwidthRemaining, Configuration.MaxLocalTxBandwidth);
            DistributeTargetTxBandwidthOverUserP2pConnections_InitiateAdjustmentRequestsIfNeeded(ref targetTxBandwidthRemaining);

            _latest_targetTxBandwidthRemaining_ForPassivePeers = targetTxBandwidthRemaining;

            DistributeTargetTxBandwidthOverPassivePeers(targetTxBandwidthRemaining);
        }

        DateTime? _lastTimeInitiatedAdjustmentRequests;
        void DistributeTargetTxBandwidthOverUserP2pConnections_InitiateAdjustmentRequestsIfNeeded(ref float targetTxBandwidthRemaining)
        {
            var minVersionDate = new DateTime(2019, 11, 17); // before this version user peers are unable for symbiosis
            var u2uConnectedPeers = (from cp in ConnectedPeers                                        
                                         select new
                                         {
                                             cp,
                                             streams = cp.Streams
                                                  .Where(x => x.LatestRemoteStatus?.IhavePassiveRole == false && x.StreamIsIdleCached == false)
                                                  .ToArray()
                                         }
                ).Where(cp => cp.streams.Length != 0 && cp.cp.RemoteLibraryVersion > minVersionDate).ToArray();


            foreach (var cp in u2uConnectedPeers)
                foreach (var s in cp.streams)
                {
                    targetTxBandwidthRemaining -= s.TargetTxBandwidth;
                }

            #region initiate adjustment requests
            var dtNowUTC = LocalPeer.DateTimeNowUtc;
            if (_lastTimeInitiatedAdjustmentRequests == null || (dtNowUTC - _lastTimeInitiatedAdjustmentRequests.Value).TotalSeconds > 2)
            {
                _lastTimeInitiatedAdjustmentRequests = dtNowUTC;

                // every 2 secs:
                if (targetTxBandwidthRemaining > 100000)
                {
                    // try to send 100kbps adjustment request to a random user peer who is ready for this, and which has TargetTxBandwidth = 0 (via all streams)
                    var u2uConnectedPeers1 = (from cp in u2uConnectedPeers
                                              let streams = cp.streams
                                                      .Where(x => x.LatestRemoteStatus?.ImHealthyAndReadyFor100kbpsU2uSymbiosis == true && x.TargetTxBandwidth == 0
                                                            && x.NotReceivedAdjustmentRequestFromRemoteSideRecently(dtNowUTC)                                                      
                                                      )
                                                      .ToArray()
                                             select new
                                             {
                                                 cp.cp,
                                                 streams,
                                                 streamsTargetBandwidth = streams.Sum(x => x.TargetTxBandwidth)
                                             }
                        ).Where(cp => cp.streams.Length != 0 && cp.streamsTargetBandwidth == 0).ToArray();
                    if (u2uConnectedPeers1.Length != 0)
                    {
                        var cp2 = u2uConnectedPeers1[LocalPeer.Random.Next(u2uConnectedPeers1.Length)];
                        if (!cp2.streams.Any(s => s.PendingAdjustmentRequestPacketData != null))
                        {
                            var stream = cp2.streams[LocalPeer.Random.Next(cp2.streams.Length)];
                            WriteToLog($"first-time incrementing bandwidth via {stream}");
                            stream.SendBandwidthAdjustmentRequest_OnResponseAdjustLocalTxBw(stream.TargetTxBandwidth + 100000);
                        }
                    }
                    else 
                    {
                        // if not found: try to send   TargetTxBandwidth+100kbps request  to a random user peer who is ready for this and has a MINIMAL TargetTxBandwidth (via all streams)
                        //      AND if packet loss is less than 1%

                        var u2uConnectedPeers2 = (from cp in u2uConnectedPeers
                                                  let streams = cp.streams
                                                          .Where(x => x.LatestRemoteStatus?.ImHealthyAndReadyFor100kbpsU2uSymbiosis == true && x.NotReceivedAdjustmentRequestFromRemoteSideRecently(dtNowUTC))
                                                          .ToArray()
                                                  select new
                                                  {
                                                      cp.cp,
                                                      streams,
                                                      streamsTargetBandwidth = streams.Sum(x => x.TargetTxBandwidth)
                                                  }                                                   
                            ).Where(x => x.streams.Length != 0 && x.cp.StreamsAveragePacketLoss < 0.01).OrderBy(x => x.streamsTargetBandwidth).ToArray();
                        if (u2uConnectedPeers2.Length != 0)
                        {
                            var stream = u2uConnectedPeers2[0].streams.OrderBy(x => x.TargetTxBandwidth).First();
                            WriteToLog($"further incrementing bandwidth via {stream}");
                            stream.SendBandwidthAdjustmentRequest_OnResponseAdjustLocalTxBw(stream.TargetTxBandwidth + 100000);
                        }                      
                    }    
                }
                else if (targetTxBandwidthRemaining < -30000)
                {
                    // send -100kbps adjustment request to a random user peer which has MAXIMAL TargetTxBandwidth
                    var u2uConnectedPeer_withMaxBandwidth = (from cp in u2uConnectedPeers
                                              let streams = cp.streams.Where(x => x.NotReceivedAdjustmentRequestFromRemoteSideRecently(dtNowUTC)).ToArray()
                                              select new
                                              {
                                                  cp.cp,
                                                  cp.streams,
                                                  streamsTargetBandwidth = cp.streams.Sum(x => x.TargetTxBandwidth)
                                              }
                          ).Where(x => x.streams.Length != 0).OrderByDescending(x => x.streamsTargetBandwidth).FirstOrDefault();
                    if (u2uConnectedPeer_withMaxBandwidth != null)
                    {
                        var stream = u2uConnectedPeer_withMaxBandwidth.streams.OrderByDescending(x => x.TargetTxBandwidth).First(); // select stream with MAXIMAL bandwidth
                        var reqBw = Math.Max(0, stream.TargetTxBandwidth - 100000);
                        if (reqBw < 10000) reqBw = 0;
                        WriteToLog($"decrementing bandwidth via {stream}: low user-set target BW");
                        stream.SendBandwidthAdjustmentRequest_OnResponseAdjustLocalTxBw(reqBw);
                    }
                }

                //   decrement BW of U2U streams with bad packet loss:   -10kbps
                foreach (var cp in u2uConnectedPeers)
                    foreach (var s in cp.streams)
                        if (s.PendingAdjustmentRequestPacketData == null && s.TargetTxBandwidth > 0 && (s.RecentPacketLoss > 0.05 || s.LatestRemoteStatus?.RecentRxPacketLoss > 0.05))
                        {
                            var reqBw = Math.Max(0, s.TargetTxBandwidth - 10000);
                            if (reqBw < 10000) reqBw = 0;
                            WriteToLog($"decrementing bandwidth via {s}: low quality");
                            s.SendBandwidthAdjustmentRequest_OnResponseAdjustLocalTxBw(reqBw);
                        }
            }
            #endregion
        }

        void DistributeTargetTxBandwidthOverPassivePeers(float targetTxBandwidthRemaining)
        {
            var passiveConnectedPeers = (from cp in ConnectedPeers
                                  orderby cp.Type == ConnectedPeerType.toConfiguredServer ? 0 : 1
                                  select new
                                  {
                                      cp,
                                      streams = cp.Streams.Where(x => x.LatestRemoteStatus?.IhavePassiveRole == true)//.OrderBy(x => x.StreamId.Id)//.Where(x => StreamIsGoodForSubt(x, now64))
                                        .ToArray()
                                  }
                ).Where(cp => cp.streams.Length != 0).ToArray();

            int numberOfStreams = 0;
            foreach (var passiveConnectedPeer in passiveConnectedPeers)
            {
                numberOfStreams += passiveConnectedPeer.streams.Length;
                foreach (var s in passiveConnectedPeer.streams)
                    s.TargetTxBandwidth = 0;               
            }

            // initial distribution of SubtLogicConfiguration.PerStreamMinRecommendedBandwidth per peer
            foreach (var passiveConnectedPeer in passiveConnectedPeers)
            {
                var s = passiveConnectedPeer.streams[0];                
                var bw = Math.Min(targetTxBandwidthRemaining, SubtLogicConfiguration.PerStreamMinRecommendedBandwidth);
                s.TargetTxBandwidth = bw;
                targetTxBandwidthRemaining -= bw;
                if (targetTxBandwidthRemaining <= 0) break;
            }

            // initial distribution of SubtLogicConfiguration.PerStreamMinRecommendedBandwidth per extra streams
            if (targetTxBandwidthRemaining > 0)
            {
                foreach (var passiveConnectedPeer in passiveConnectedPeers)
                    for (int si = 1; si < passiveConnectedPeer.streams.Length; si++)
                    {
                        var s = passiveConnectedPeer.streams[si];
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

        }

        float _latest_targetTxBandwidthRemaining_ForPassivePeers;
        bool _isHealthyForU2uSymbiosis; // is set to true when it is healthy for some long time
        DateTime? _healthySinceUtc;
        void IsHealthyForU2uSymbiosis_Update(SubtMeasurement measurement)
        {
            if (!LocalPeer.Configuration.RoleAsUser) return;

            var isHealthyNow = (measurement.RxBandwidth > Configuration.BandwidthTarget * 0.9 &&
                measurement.TxBandwidth > Configuration.BandwidthTarget * 0.9 && Configuration.BandwidthTarget > 100000 &&
                measurement.RxPacketLoss < 0.05 && measurement.TxPacketLoss < 0.05
                );
            if (isHealthyNow)
            {
                var dt = LocalPeer.DateTimeNowUtc;
                if (_healthySinceUtc == null) _healthySinceUtc = dt;
                else
                {
                    if ((dt - _healthySinceUtc.Value).TotalSeconds > 30)
                    {
                        _isHealthyForU2uSymbiosis = true;
                    }
                }
            }
            else
            {
                _healthySinceUtc = null;
                _isHealthyForU2uSymbiosis = false;
            }
        }
        public bool ImHealthyAndReadyFor100kbpsU2uSymbiosis
        {
            get // sender thread
            {
                if (!_isHealthyForU2uSymbiosis) return false;
                if (_latest_targetTxBandwidthRemaining_ForPassivePeers <= 100000) return false;

                return true;
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
        internal SubtMeasurement LatestMeasurement { get; private set; }
    }

}
