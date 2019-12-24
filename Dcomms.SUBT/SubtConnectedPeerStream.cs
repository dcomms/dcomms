 using Dcomms.P2PTP;
using Dcomms.DSP;
using Dcomms.P2PTP.Extensibility;
using Dcomms.P2PTP.LocalLogic;
using Dcomms.SUBT.SUBTP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Dcomms.SUBT
{
    /// <summary>
    /// stores measurements, signaling state of remote stream
    /// </summary>
    public class SubtConnectedPeerStream : IConnectedPeerStreamExtension
    {
        readonly IConnectedPeerStream _stream;
        public IConnectedPeerStream Stream => _stream;
       
        readonly SubtSenderThread _senderThread;
        internal StreamId StreamId => _stream.StreamId;
        internal readonly SubtConnectedPeer SubtConnectedPeer;
        internal readonly SubtLocalPeer SubtLocalPeer;
        public SubtConnectedPeerStream(IConnectedPeerStream stream, SubtLocalPeer subtLocalPeer, SubtConnectedPeer subtConnectedPeer)
        {
            SubtConnectedPeer = subtConnectedPeer;
            SubtLocalPeer = subtLocalPeer;
            _stream = stream;
            InitializePayloadPacket();

            _txSequence = (ushort)subtLocalPeer.LocalPeer.Random.Next(ushort.MaxValue);
            _rxMeasurement = new RxMeasurement(subtLocalPeer, this);
           
            _senderThread = subtLocalPeer.SenderThreadForNewStream;
            _senderThread.OnCreatedDestroyedStream(this, true);
        }

        public override string ToString() => _stream.ToString();
        //{
        //    return $"{_stream}:targetTxBw={TargetTxBandwidth.BandwidthToString()},";
        //}

        #region rx payload packets, RxMeasurement
        readonly RxMeasurement _rxMeasurement;
        /// <summary>
        /// tx payload packets   transmitted with timestamp32  ---> remote peer reflects it to "receivedTimestamp32" --> sends back to this peer --> we have the difference
        /// </summary>
        internal TimeSpan? RecentRtt { get; private set; }
        internal TimeSpan? RecentRttConsideringP2ptp
        {
            get
            {
                var r = _stream.LatestHelloRtt;
                if (r == null || RecentRtt < r.Value)
                    r = RecentRtt;
                return r;
            }
        }
        public string RecentRttString => MiscProcedures.TimeSpanToString(RecentRttConsideringP2ptp);
        internal float RecentRxBandwidth => _rxMeasurement.RecentBandwidth;
        public string RecentRxBandwidthString => $"{_rxBwBeforeJB.OutputPerUnit.BandwidthToString()}/{_rxMeasurement.RecentBandwidth.BandwidthToString()}";
        internal float RecentPacketLoss => _rxMeasurement.RecentPacketLoss; // 0..1
        public string RecentPacketLossString => String.Format("{0:0.00}%", RecentPacketLoss*100);

        IirFilterCounter _rxBwBeforeJB = new IirFilterCounter(TimeSpan.TicksPerMillisecond * 500, TimeSpan.TicksPerSecond); // locked
        void IConnectedPeerStreamExtension.OnReceivedPayloadPacket(byte[] data, int index)
        {
            _stream.MarkAsActiveByExtension();
            //   _totalUdpBytesReceived += (UInt64)data.Length;

            var timeNow32 = SubtLocalPeer.LocalPeer.Time32;
            lock (_rxBwBeforeJB)
            {
                _rxBwBeforeJB.Input((data.Length + LocalLogicConfiguration.IpAndUdpHeadersSizeBytes) * 8);
                _rxBwBeforeJB.OnTimeObserved(timeNow32);
            }

            var timestamp32 = BinaryProcedures.DecodeUInt32(data, ref index);
            _timestamp32ToReflect = timestamp32;
            var sequence = BinaryProcedures.DecodeUInt16(data, ref index);
            var reflectedTimestamp32 = BinaryProcedures.DecodeUInt32(data, ref index);
            if (reflectedTimestamp32 != 0)
            {
                RecentRtt = TimeSpan.FromTicks(unchecked(SubtLocalPeer.LocalPeer.Time32 - reflectedTimestamp32));
            }

            _rxMeasurement.OnReceivedPacket((ushort)(data.Length * 8 + LocalLogicConfiguration.IpAndUdpHeadersSizeBits), sequence, timestamp32, timeNow32);
        }
        #endregion
        
        public string TxBandwidthString
        {
            get
            {
             //   if (Stream.Debug)
             //       Debugger.Break();

                var r = $"{RecentTxBandwidth.BandwidthToString()}/{TargetTxBandwidth.BandwidthToString()}/";
                if (SubtLocalPeer.LocalPeer.Configuration.RoleAsSharedPassive) r += "passive";
                else r += (TargetTxBandwidthLatestMultiplier - 1).ToString("E2");
                return r;
            }
        }
       
        #region tx payload packets
        /// <summary>
        /// format:
        /// SUBT payload header
        /// stream ID 4 bytes
        /// seq 2 bytes
        /// timestamp 4 bytes
        /// random data
        /// </summary>
        byte[] _payloadPacket;
        int _payloadPacketIndex;
        void InitializePayloadPacket()
        {
            _payloadPacket = new byte[LocalLogicConfiguration.UdpMaxPacketSizeWithoutHeadersBytes];
            var rnd = new Random();
            rnd.NextBytes(_payloadPacket);            
            _payloadPacketIndex = ExtensionProcedures.InitializePayloadPacketForExtension(PacketHeaders.SubtPayload, _payloadPacket, _stream.StreamId);
        }
        
        class TxConfiguration
        {
            public int PacketsPer10ms;
            public int PacketsPer100ms;
            public int PacketsPer1s;
            public int UdpBytesPerPacket10ms; // must be greater than PayloadPacketHeaderSizeBytes
            public int UdpBytesPerPacket100ms; // must be greater than PayloadPacketHeaderSizeBytes
            public int UdpBytesPerPacket1s; // must be greater than PayloadPacketHeaderSizeBytes
        }
        TxConfiguration _txConfiguration;


        void ConfigureTx_100ms_1s(TxConfiguration txConfiguration, float bw)
        {
            var packetsPer100ms_fullPayload = (int)Math.Floor(bw * 0.1f / (LocalLogicConfiguration.IpAndUdpHeadersSizeBytes + _payloadPacket.Length) / 8);
            var udpBytesPerPacket100ms = (int)Math.Floor(bw * 0.1f / 8 - LocalLogicConfiguration.IpAndUdpHeadersSizeBytes);
            if (packetsPer100ms_fullPayload < 5 && udpBytesPerPacket100ms <= _payloadPacket.Length)
            {
                if (udpBytesPerPacket100ms > PayloadPacketHeaderSizeBytes && udpBytesPerPacket100ms > 200)
                {
                    txConfiguration.PacketsPer100ms = 1;
                    txConfiguration.UdpBytesPerPacket100ms = udpBytesPerPacket100ms;
                }
                else
                { // not 100ms, go with 1s period
                    var packetsPer1s_fullPayload = (int)Math.Floor(bw / (LocalLogicConfiguration.IpAndUdpHeadersSizeBytes + _payloadPacket.Length) / 8);
                    var udpBytesPerPacket1s = (int)Math.Floor(bw / 8 - LocalLogicConfiguration.IpAndUdpHeadersSizeBytes);
                    if (packetsPer1s_fullPayload < 5 && udpBytesPerPacket1s <= _payloadPacket.Length)
                    {
                        if (udpBytesPerPacket1s > PayloadPacketHeaderSizeBytes)
                        {
                            txConfiguration.PacketsPer1s = 1;
                            txConfiguration.UdpBytesPerPacket1s = udpBytesPerPacket1s;
                        }
                        else
                        {
                            // lowest bandwidth
                            txConfiguration.PacketsPer1s = 1;
                            txConfiguration.UdpBytesPerPacket1s = PayloadPacketHeaderSizeBytes;
                        }
                    }
                    else
                    {
                        txConfiguration.PacketsPer1s = packetsPer1s_fullPayload;
                        txConfiguration.UdpBytesPerPacket1s = _payloadPacket.Length;

                        // todo more exact
                    }
                }
            }
            else
            {
                txConfiguration.PacketsPer100ms = packetsPer100ms_fullPayload;
                txConfiguration.UdpBytesPerPacket100ms = _payloadPacket.Length;
                        
                // todo precise adjustment
            }
        }
        void ConfigureTx(TxConfiguration txConfiguration, float bw)
        {
            var packetsPer10ms_fullPayload = (int)Math.Floor(bw * 0.01f / (LocalLogicConfiguration.IpAndUdpHeadersSizeBytes + _payloadPacket.Length) / 8);
            var udpBytesPerPacket10ms = (int)Math.Floor(bw * 0.01f / 8 - LocalLogicConfiguration.IpAndUdpHeadersSizeBytes);
            if (packetsPer10ms_fullPayload < 5 && udpBytesPerPacket10ms <= _payloadPacket.Length)
            {
                if (udpBytesPerPacket10ms > PayloadPacketHeaderSizeBytes && udpBytesPerPacket10ms > 200)
                {
                    txConfiguration.PacketsPer10ms = 1;
                    txConfiguration.UdpBytesPerPacket10ms = udpBytesPerPacket10ms;
                }
                else
                { // not 10ms,  go with 100ms period
                    ConfigureTx_100ms_1s(txConfiguration, bw);
                }
            }
            else // large bandwidths
            {
                txConfiguration.PacketsPer10ms = packetsPer10ms_fullPayload;
                txConfiguration.UdpBytesPerPacket10ms = _payloadPacket.Length;

                // precise adjustment
                var remainderBw = bw - 100.0f * txConfiguration.PacketsPer10ms * (txConfiguration.UdpBytesPerPacket10ms + LocalLogicConfiguration.IpAndUdpHeadersSizeBytes) * 8;
                ConfigureTx_100ms_1s(txConfiguration, remainderBw);
            }
        }


        float _targetTxBandwidth;
        public float TargetTxBandwidth
        {
            get { return _targetTxBandwidth; }
            set
            {
                if (float.IsInfinity(value)) throw new ArgumentException(nameof(value));
                if (float.IsNaN(value)) throw new ArgumentException(nameof(value));
                if (value > 100000000) throw new ArgumentException(nameof(value));

                if (_targetTxBandwidth == value) return;
                _targetTxBandwidth = value;

                _recentTxBandwidth.OutputPerUnit = value;

                TxConfiguration newTxConfiguration = new TxConfiguration();
                ConfigureTx(newTxConfiguration, value);
                _txConfiguration = newTxConfiguration;
            }
        }
        public float TargetTxBandwidthLatestMultiplier { get; set; } = 1;
        
        readonly IirFilterCounter _recentTxBandwidth = new IirFilterCounter(SubtLogicConfiguration.RecentTxBandwidthDecayTimeTicks, TimeSpan.TicksPerSecond); // self-test
        public float RecentTxBandwidth => _recentTxBandwidth.OutputPerUnit;
      

        uint _timestamp32ToReflect = 0;
        ushort _txSequence;

        const int PayloadPacketHeaderSizeBytes = 2 + // header
            4 + // stream ID
            4 + // timestamp
            2 + // seq 
            4   // reflected timestamp
            ;

        internal bool TxIsEnabled => (Stream.RemotePeerRoleIsUser || SubtLocalPeer.LocalPeer.Configuration.RoleAsUser);
        internal void SendPacketsIfNeeded_10ms() // sender thread
        {
            var timeNow32 = SubtLocalPeer.LocalPeer.Time32;
            SendStatusIfNeeded(timeNow32);
            RetransmitBandwidthAdjustmentRequestIfNeeded(timeNow32);

            lock (_rxBwBeforeJB)
                _rxBwBeforeJB.OnTimeObserved(timeNow32);

            _rxMeasurement.OnTimer_SenderThread(timeNow32);

            if (!TxIsEnabled) return;
            if (SubtConnectedPeer.RemotePeerId != null && _txConfiguration != null) // check if handshaking is complete
            {
                for (int i = 0; i < _txConfiguration.PacketsPer10ms; i++)
                    SendPayloadPacket(_txConfiguration.UdpBytesPerPacket10ms);
            }
        }
        internal void SendPayloadPacketsIfNeeded_100ms() // sender thread
        {
            if (!TxIsEnabled) return;
            if (SubtConnectedPeer.RemotePeerId != null && _txConfiguration != null) // check if handshaking is complete
            {
                for (int i = 0; i < _txConfiguration.PacketsPer100ms; i++)
                    SendPayloadPacket(_txConfiguration.UdpBytesPerPacket100ms);
            }
        }
        internal void SendPayloadPacketsIfNeeded_1s() // sender thread
        {
            StreamIsIdleCached = Stream.IsIdle(SubtLocalPeer.LocalPeer.DateTimeNowUtc, SubtLogicConfiguration.MaxPeerIdleTime_TxPayload);
            if (StreamIsIdleCached)
                LatestRemoteStatus = null;

            if (!TxIsEnabled) return;
            if (SubtConnectedPeer.RemotePeerId != null && _txConfiguration != null) // check if handshaking is complete
            {
                for (int i = 0; i < _txConfiguration.PacketsPer1s; i++)
                    SendPayloadPacket(_txConfiguration.UdpBytesPerPacket1s);
            }
        }

        internal bool StreamIsIdleCached { get; private set; } 
        void SendPayloadPacket(int length)
        {
            var timestampNow32 = SubtLocalPeer.LocalPeer.Time32;        
            _recentTxBandwidth.OnTimeObserved(timestampNow32);
        
            if (StreamIsIdleCached == false)
            {
                _txSequence++;
                var index = _payloadPacketIndex;
                BinaryProcedures.EncodeUInt32(_payloadPacket, ref index, timestampNow32);
                BinaryProcedures.EncodeUInt16(_payloadPacket, ref index, _txSequence);
                BinaryProcedures.EncodeUInt32(_payloadPacket, ref index, _timestamp32ToReflect);
                _stream.SendPacket(_payloadPacket, length);

                _recentTxBandwidth.Input(LocalLogicConfiguration.IpAndUdpHeadersSizeBits + length * 8);

                if (Stream.Debug)
                    Debugger.Break();

            }
        }
        #endregion

        #region send status/measurements
        uint? _lastTimeSentStatus = null;
        SubtRemoteStatusPacket _lastSentSubtStatusPacket;
        void SendStatusIfNeeded(uint timestamp32) // sender thread
        {
            if (_lastTimeSentStatus == null ||
                MiscProcedures.TimeStamp1IsLess(_lastTimeSentStatus.Value + SubtLogicConfiguration.SubtRemoteStatusPacketTransmissionIntervalTicks, timestamp32)
                )
            {
                _lastTimeSentStatus = timestamp32;
                var remotePeerId = SubtConnectedPeer.RemotePeerId;
                if (remotePeerId != null)
                {
                    bool samePacketAlreadySent = false;
                    if (_lastSentSubtStatusPacket != null)
                    {
                        if (_lastSentSubtStatusPacket.ImHealthyAndReadyFor100kbpsU2uSymbiosis == SubtLocalPeer.ImHealthyAndReadyFor100kbpsU2uSymbiosis &&
                            _lastSentSubtStatusPacket.RecentTxBandwidth == this.RecentTxBandwidth &&
                            _lastSentSubtStatusPacket.RecentRxBandwidth == _rxMeasurement.RecentBandwidth)
                                samePacketAlreadySent = true;
                    }

                    if (!samePacketAlreadySent)
                    {
                        var statusPacket = new SubtRemoteStatusPacket(_rxMeasurement.RecentBandwidth, _rxMeasurement.RecentPacketLoss,
                                this.RecentTxBandwidth,
                                SubtLocalPeer.LocalPeer.Configuration.RoleAsSharedPassive,
                                SubtLocalPeer.ImHealthyAndReadyFor100kbpsU2uSymbiosis
                                );
                      //  SubtLocalPeer.WriteToLog_deepDetail($"sending SUBT status packet: {statusPacket} to peer {SubtConnectedPeer.RemotePeerId}");
                        var data = statusPacket.Encode(this);
                        _stream.SendPacket(data, data.Length);
                        _lastSentSubtStatusPacket = statusPacket;
                    }
                }
            }
        }        
        #endregion

        //internal void SendAdjustmentSignal(float relativeAdjustmentRequest, float absoluteAdjustmentRequest)
        //{
        //    var data = new SubtPacketAdjustmentSignal(relativeAdjustmentRequest, absoluteAdjustmentRequest).Encode(this);
        //    _stream.SendPacket(data);
        //}

        internal SubtRemoteStatusPacket LatestRemoteStatus;
        public string LatestRemoteTxStatusString => LatestRemoteStatus?.RecentTxBandwidth.BandwidthToString();
        public string LatestRemoteRxStatusString => LatestRemoteStatus?.RecentRxBandwidth.BandwidthToString() + " " +
            LatestRemoteStatus?.RecentRxPacketLoss.PacketLossToString();
        void IConnectedPeerStreamExtension.OnReceivedSignalingPacket(BinaryReader reader) // manager thread
        {
            var subtPacketType = (SubtPacketType)reader.ReadByte();

            switch (subtPacketType)
            {
                case SubtPacketType.RemoteStatus:
                    var p = new SubtRemoteStatusPacket(reader);
                   // SubtLocalPeer.WriteToLog_deepDetail($"received from peer {SubtConnectedPeer.RemotePeerId}: SUBT status packet: {p}");
                    LatestRemoteStatus = p;
                    _stream.MarkAsActiveByExtension();

                    if (_rxBwBeforeJB.OutputPerUnit > p.RecentTxBandwidth * 5)
                        EmitPainToDeveloper($"_rxBwBeforeJB.OutputPerUnit={_rxBwBeforeJB.OutputPerUnit} > p.RecentTxBandwidth={p.RecentTxBandwidth}");
                    break;
                case SubtPacketType.AdjustmentRequest:
                    var adj = new AdjustmentRequestPacket(reader);
                    SubtLocalPeer.WriteToLog_deepDetail($"{this} received adjustment request: {adj}");
                    if (adj.TxTargetBandwidth > this.TargetTxBandwidth)
                    { // increase
                        if (SubtLocalPeer.ImHealthyAndReadyFor100kbpsU2uSymbiosis)
                        {
                            // check requested BW
                            this.TargetTxBandwidth = Math.Min(adj.TxTargetBandwidth, MaxTxBandwidthToAcceptFromRemoteSide);
                            SubtLocalPeer.WriteToLog_deepDetail($"{this} bandwidth increased to {MiscProcedures.BandwidthToString(this.TargetTxBandwidth)}");
                        }
                        else
                            SubtLocalPeer.WriteToLog_deepDetail($"{this} is not healthy to increase bandwidth");
                    }
                    else // decrease
                    {
                        this.TargetTxBandwidth = adj.TxTargetBandwidth;
                        SubtLocalPeer.WriteToLog_deepDetail($"{this} bandwidth decreased to {MiscProcedures.BandwidthToString(this.TargetTxBandwidth)}");
                    }

                    // respond
                    var resp = new AdjustmentResponsePacket(this.TargetTxBandwidth);
                    var respData = resp.Encode(this);
                    _stream.SendPacket(respData, respData.Length);
                    _lastTimeReceivedAdjustmentRequestUTC = SubtLocalPeer.LocalPeer.DateTimeNowUtc;
                    break;
                case SubtPacketType.AdjustmentResponse:
                    var adjResp = new AdjustmentResponsePacket(reader);
                    if (PendingAdjustmentRequestPacket != null)
                    { // we got response from remote peer
                        SubtLocalPeer.WriteToLog_deepDetail($"{this} received adjustment response: {adjResp}");
                        //  adjust local tx BW, according to remote BW. check what is responded 
                        this.TargetTxBandwidth = Math.Min(adjResp.TxTargetBandwidth, PendingAdjustmentRequestPacket.TxTargetBandwidth);
                        PendingAdjustmentRequestPacket = null;
                        PendingAdjustmentRequestPacketData = null;
                    }
                    break;
            }
        }
        float MaxTxBandwidthToAcceptFromRemoteSide => SubtLocalPeer.LatestMeasurement?.RxTxMinBandwidth * 0.7f ?? 0;
        DateTime? _lastTimeReceivedAdjustmentRequestUTC;
        internal bool NotReceivedAdjustmentRequestFromRemoteSideRecently(DateTime nowUTC)
        {
            if (_lastTimeReceivedAdjustmentRequestUTC == null) return true;
            return (nowUTC - _lastTimeReceivedAdjustmentRequestUTC.Value).TotalSeconds > 2;
        }

        void EmitPainToDeveloper(string message)
        {//todo
           // SubtLocalPeer.SignalErrorToDeveloper();
        }

        public void OnDestroyed()
        {
            _senderThread.OnCreatedDestroyedStream(this, false);
        }

        internal byte[] PendingAdjustmentRequestPacketData; // is not null if request is not responded yet, and is retransmitted
        internal AdjustmentRequestPacket PendingAdjustmentRequestPacket;
        public void SendBandwidthAdjustmentRequest_OnResponseAdjustLocalTxBw(float requestedTxBandwidthAtRemotePeer)
        {
            var remotePeerId = SubtConnectedPeer.RemotePeerId;
            if (remotePeerId != null)
            {
                PendingAdjustmentRequestPacket = new AdjustmentRequestPacket(requestedTxBandwidthAtRemotePeer);
                SubtLocalPeer.WriteToLog_deepDetail($"{this} sends adjustment request {PendingAdjustmentRequestPacket}");
                PendingAdjustmentRequestPacketData = PendingAdjustmentRequestPacket.Encode(this);
                _stream.SendPacket(PendingAdjustmentRequestPacketData, PendingAdjustmentRequestPacketData.Length);
            }
        }
        uint? _lastTimeSentBandwidthAdjustmentRequest = null;
        void RetransmitBandwidthAdjustmentRequestIfNeeded(uint timestamp32) // sender thread
        {
            var p = PendingAdjustmentRequestPacketData; // save it to this (sender) thread
            if (p != null && _lastTimeSentBandwidthAdjustmentRequest != null &&
                MiscProcedures.TimeStamp1IsLess(_lastTimeSentBandwidthAdjustmentRequest.Value + SubtLogicConfiguration.SubtAdjustmentRequestRetransmissionIntervalTicks, timestamp32)
                )
            {
                _lastTimeSentBandwidthAdjustmentRequest = timestamp32;
                _stream.SendPacket(p, p.Length);
            }
        }
    }
}
