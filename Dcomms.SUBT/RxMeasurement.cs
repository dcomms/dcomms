using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Dcomms.SUBT
{
    /// <summary>
    /// measures quality (packet loss, optionally jitter) for ConnectedPeerStream
    /// </summary>
    internal class RxMeasurement
    {
        /// <summary>
        /// stores timestamp and sequence fields of previously received packets
        /// </summary>
        [DebuggerDisplay("seq={sequence},ts={timestamp32}={timestamp32_hex}")]
        class JitterBufferElement
        {
            public uint timestamp32;
            public string timestamp32_hex => String.Format("{0:x8}", timestamp32);
            public ushort sequence;
            public ushort bandwidthSizeBits;
            public override string ToString()
            {
                return $"seq={sequence},ts={timestamp32}={timestamp32_hex}";
            }
        }
        static uint SubtractTimestamps(uint t1, uint t2)
        {
            return unchecked(t1 - t2);
        }
        static bool Sequence1IsLess(ushort seq1, ushort seq2)
        {
            //    seq1        seq2   result
            //    ffff        fffe   false
            //    0000        ffff   false
            //    ffff        0000   true
            //    0001        0000   false

            if (seq1 > 0xCFFF && seq2 < 0x3FFF)
                return true;
            if (seq1 < 0x3FFF && seq2 > 0xCFFF)
                return false;

            return seq1 < seq2;
        }
        static ushort SubtractSequences(ushort s1, ushort s2)
        {
            return unchecked((ushort)(s1 - s2));
        }

        /// <summary>
        /// contains info about previously received packets, ordered by sequence, from oldest to newest
        /// accessed only by receiver thread
        /// </summary>
        LinkedList<JitterBufferElement> _jitterBuffer = new LinkedList<JitterBufferElement>();
        void CheckJitterBuffer()
        {
            ushort? previousSeq = null;
            uint? previousTs32 = null;
            foreach (var jbe in _jitterBuffer)
            {
                if (previousSeq.HasValue && Sequence1IsLess(jbe.sequence, previousSeq.Value))
                    throw new Exception();
                if (previousTs32.HasValue && MiscProcedures.TimeStamp1IsLess(jbe.timestamp32, previousTs32.Value))
                    throw new Exception();

                previousSeq = jbe.sequence;
                previousTs32 = jbe.timestamp32;
            }
        } 

        readonly IirFilterCounter _recentBandwidth = new IirFilterCounter(SubtLogicConfiguration.RecenRxBandwidthDecayTimeTicks, TimeSpan.TicksPerSecond); // locked
        internal float RecentBandwidth => _recentBandwidth.OutputPerUnit;

        bool IsNewElementOutOfSequence(JitterBufferElement jbe)
        {
            if (_jitterBuffer.Count != 0)
            {
                var firstSeq = _jitterBuffer.First.Value.sequence;
                var lastSeq = _jitterBuffer.Last.Value.sequence;

                const ushort maxDistance = 1000; // TODO why 1000??
                var minSequence = unchecked((ushort)(firstSeq - maxDistance));
                if (Sequence1IsLess(jbe.sequence, minSequence)) return true;
                var maxSequence = unchecked((ushort)(lastSeq + maxDistance));
                if (Sequence1IsLess(maxSequence, jbe.sequence)) return true;
                return false;
            }

            return false;
        }
        bool TryInsertIntoJitterBuffer(JitterBufferElement jbe)
        {
            // _subtLocalPeer.WriteToLog($">> TryInsertIntoJitterBuffer strm{_stream.StreamId} count={_jitterBuffer.Count} ts={jbe.timestamp32} seq={jbe.sequence} last (newest) TS={_jitterBuffer.Last?.Value?.timestamp32}");

            if (IsNewElementOutOfSequence(jbe)) return false;

            LinkedListNode<JitterBufferElement> insertAfter = null;
            for (var item = _jitterBuffer.Last; ;)
            {
                if (item == null) break;
                var enumeratedSequence = item.Value.sequence;
                if (enumeratedSequence == jbe.sequence) return false; // duplicate packet
                if (Sequence1IsLess(enumeratedSequence, jbe.sequence))
                {
                    insertAfter = item;
                    break;
                }
                item = item.Previous;
            }

            if (insertAfter == null)
            {
                //if (_jitterBuffer.First != null)
                //{
                //    if (TimeStamp1IsLess(_jitterBuffer.First.Value.timestamp32, jbe.timestamp32))
                //    {
                //        // throw new Exception();
                //        _subtLocalPeer.WriteToLog($"strm{_stream.StreamId} count={_jitterBuffer.Count}. bad item put into JB: ts={jbe.timestamp32} seq={jbe.sequence} firstTS={_jitterBuffer.First.Value.timestamp32}  firstSeq={_jitterBuffer.First.Value.sequence} ");
                //        foreach (var item in _jitterBuffer)
                //            _subtLocalPeer.WriteToLog($"strm{_stream.StreamId} item: TS={item.timestamp32}  seq={item.sequence} ");                        
                //        return false;
                //    }
                //}
                _jitterBuffer.AddFirst(jbe);
            }
            else
            {
                if (MiscProcedures.TimeStamp1IsLess(jbe.timestamp32, insertAfter.Value.timestamp32))
                {
                    _subtLocalPeer.WriteToLog_lightPain($"JB corruption check: received {jbe}; first: {_jitterBuffer.First?.Value}; last: {_jitterBuffer.Last?.Value}");
                    return false;
                    //throw new Exception();
                }
                _jitterBuffer.AddAfter(insertAfter, jbe);
            }

            //  if (_stream.Stream.Debug)
            //        CheckJitterBuffer();

            if (_jitterBuffer.Count > 5000)
                _subtLocalPeer.WriteToLog_mediumPain($"<< TryInsertIntoJitterBuffer strm{_stream.StreamId} count={_jitterBuffer.Count} ts={jbe.timestamp32} seq={jbe.sequence} last (newest) TS={_jitterBuffer.Last?.Value?.timestamp32}");
            else if (_jitterBuffer.Count > 1000)
                _subtLocalPeer.WriteToLog_lightPain($"<< TryInsertIntoJitterBuffer strm{_stream.StreamId} count={_jitterBuffer.Count} ts={jbe.timestamp32} seq={jbe.sequence} last (newest) TS={_jitterBuffer.Last?.Value?.timestamp32}");
            else if (_jitterBuffer.Count > 100)
                _subtLocalPeer.WriteToLog_deepDetail($"<< TryInsertIntoJitterBuffer strm{_stream.StreamId} count={_jitterBuffer.Count} ts={jbe.timestamp32} seq={jbe.sequence} last (newest) TS={_jitterBuffer.Last?.Value?.timestamp32}");


            return true;
        }
        JitterBufferElement _lastPlayedJBE = null;
        readonly IirFilterAverage _recentPacketLoss = new IirFilterAverage(SubtLogicConfiguration.RecentPacketLossDecayTimeTicks);
        internal float RecentPacketLoss => Math.Min(_recentPacketLoss.Output, 1); // 0..1
        void OnPlayed(JitterBufferElement jbe, uint timeNow32)
        {
            if (_lastPlayedJBE != null)
            {
                var missingPacketsCountAtThisPlaybackTime = SubtractSequences(jbe.sequence, _lastPlayedJBE.sequence) - 1;
            
              //  var timePassed32AtSender = SubtractTimestamps(jbe.timestamp32, _lastPlayedJBE.timestamp32);////todo better use local time

          //      if (missingPacketsCountAtThisPlaybackTime > 100)
          //          _subtLocalPeer.WriteToLog($"missingPacketsCountAtThisPlaybackTime = {missingPacketsCountAtThisPlaybackTime}");
                          
                 _recentPacketLoss.Input(missingPacketsCountAtThisPlaybackTime
                //   , timePassed32AtSender
                   );
                _recentPacketLoss.OnTimeObserved(timeNow32);
                lock (_recentBandwidth)
                {
                    _recentBandwidth.Input(jbe.bandwidthSizeBits//, timePassed32
                        );
                    _recentBandwidth.OnTimeObserved(timeNow32);
                }
            }

            _lastPlayedJBE = jbe;
        }
        void PlaybackFromJitter(uint timeNow32)
        {
          //  if (_stream.Stream.Debug) _subtLocalPeer.WriteToLog($">> PlaybackFromJitter count={_jitterBuffer.Count}");
            if (_jitterBuffer.Count != 0)
            {
                // simulate playback from JB	
                while (_jitterBuffer.Count > SubtLogicConfiguration.JitterBufferMaxElementsCount)
                {
                    var oldestItem = _jitterBuffer.First;
                    if (oldestItem == null)
                        break;  

                    _subtLocalPeer.WriteToLog_deepDetail(
                              $"<< PlaybackFromJitter count={_jitterBuffer.Count} oldestTS={oldestItem.Value.timestamp32}, timeNow32={timeNow32}");
                         
                    OnPlayed(oldestItem.Value, timeNow32);
                    _jitterBuffer.RemoveFirst();
                }
	
                var newestTimestampInSimulatedJitterBuffer = _jitterBuffer.Last.Value.timestamp32;

                // determine TS value of currently played frame
                var maxAllowedTimestampInSimulatedJitterBuffer = unchecked(newestTimestampInSimulatedJitterBuffer - SubtLogicConfiguration.JitterBufferLengthTicks);

               // if (_stream.Stream.Debug) Debugger.Break();

                // remove oldest frames which are being played
                for (;;)
                {
                    var oldestItem = _jitterBuffer.First;
                    if (oldestItem == null)
                        break;
                    if (MiscProcedures.TimeStamp1IsLess(oldestItem.Value.timestamp32, maxAllowedTimestampInSimulatedJitterBuffer) == false)
                    {
                  //      if (_stream.Stream.Debug) _subtLocalPeer.WriteToLog(
                  //          $"<< PlaybackFromJitter count={_jitterBuffer.Count} oldestTS={oldestItem.Value.timestamp32}, maxAllowedTimestampInSimulatedJitterBuffer={maxAllowedTimestampInSimulatedJitterBuffer}, newestTimestamp={newestTimestampInSimulatedJitterBuffer}");
                        break;
                    }

                    OnPlayed(oldestItem.Value, timeNow32);
                    _jitterBuffer.RemoveFirst();
                }
            }
        }

        readonly SubtLocalPeer _subtLocalPeer;
        readonly SubtConnectedPeerStream _stream;
        internal RxMeasurement(SubtLocalPeer subtLocalPeer, SubtConnectedPeerStream stream)
        {
            _subtLocalPeer = subtLocalPeer;
            _stream = stream;
        }

        /// <summary>
        /// general algorithm:
        /// find a place where to insert this packet into jitter buffer
        /// if it is duplicate, ignore the packet
        /// insert into jitter buffer
        /// simulate playback: remove elements from head of the JB
        /// calculate packet loss out of sequence fields
        /// </summary>
        internal void OnReceivedPacket(ushort bandwidthSizeBits, ushort sequence, uint timestamp32, uint timeNow32)
        {
            var jbe = new JitterBufferElement { timestamp32 = timestamp32, sequence = sequence, bandwidthSizeBits = bandwidthSizeBits };
            if (TryInsertIntoJitterBuffer(jbe))
            {
                PlaybackFromJitter(timeNow32);
            }
        }

        internal void OnTimer_SenderThread(uint timeNow32)
        {
            lock (_recentBandwidth)
                _recentBandwidth.OnTimeObserved(timeNow32);
        }
    }
}
