using Dcomms.P2PTP;
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Dcomms.SUBT
{
    /// <summary>
    /// runs thread, sends payload packets
    /// </summary>
    internal class SubtSenderThread: IDisposable
    {
        /// <summary>
        /// is executed by sender thread
        /// </summary>
        readonly ActionsQueue _actionsQueue;

        /// <summary>
        /// accessed by this sender thread only
        /// </summary>
        Dictionary<StreamId, SubtConnectedPeerStream> _streams = new Dictionary<StreamId, SubtConnectedPeerStream>();
        readonly Thread _thread;
        bool _disposing;
        readonly SubtLocalPeer _localPeer;
        bool _debug = false;
        readonly string _threadName;
        public SubtSenderThread(SubtLocalPeer localPeer, string threadName)
        {
            _threadName = threadName;
            _localPeer = localPeer;
            _actionsQueue = new ActionsQueue(exc => _localPeer.HandleException(exc));
            _thread = new Thread(ThreadEntry);
            _thread.Name = threadName;
            _thread.Start();
            _thread.Priority = ThreadPriority.Highest;

        }
        public override string ToString() => _threadName;
        void ThreadEntry()
        {
            var previousTs32 = _localPeer.LocalPeer.Time32;
            const uint period32 = (uint)TimeSpan.TicksPerMillisecond * 10;
            int counter = 0;
            while (!_disposing)
            {
                try
                {
                    _actionsQueue.ExecuteQueued();

                    var timeNow32a = _localPeer.LocalPeer.Time32;
                    while (MiscProcedures.TimeStamp1IsLess(previousTs32, timeNow32a) && !_disposing)
                    {
                        if (_debug)
                            Debugger.Break();

                        previousTs32 = unchecked(previousTs32 + period32);
                        var streamsA = _streams.Values.ToArray();
                        var timeNow32b = _localPeer.LocalPeer.Time32;
                        foreach (var stream in streamsA) stream.SendPacketsIfNeeded_10ms(timeNow32b);
                        counter++;
                        if (counter % 10 == 0)
                            foreach (var stream in streamsA) stream.SendPayloadPacketsIfNeeded_100ms(timeNow32b);
                        if (counter % 100 == 0)
                            foreach (var stream in streamsA) stream.SendPayloadPacketsIfNeeded_1s();
                    }
                }
                catch (Exception exc)
                {
                    _localPeer.HandleException(exc);
                }
                Thread.Sleep(10);
            }
        }
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;
            _thread.Join();
        }



        internal void OnCreatedDestroyedStream(SubtConnectedPeerStream stream, bool createdOrDestroyed)
        {
            _actionsQueue.Enqueue(() =>
            {
                if (createdOrDestroyed)
                {
                    if (!_streams.ContainsKey(stream.StreamId))
                    {
                        _streams.Add(stream.StreamId, stream); // todo why does it insert duplicate keys sometimes?
                        if (_localPeer.LocalPeer.Configuration.RoleAsUser)
                        {
                            if (_streams.Count > 150) _localPeer.WriteToLog_lightPain($"SUBT sender thread streams leak, count = {_streams.Count}");
                        }
                        else
                        {
                            if (_streams.Count > 1500) _localPeer.WriteToLog_lightPain($"SUBT sender thread streams leak, count = {_streams.Count}");
                        }
                    }
                }
                else _streams.Remove(stream.StreamId);
            }, "subtsender2462");

        }
    }
}
