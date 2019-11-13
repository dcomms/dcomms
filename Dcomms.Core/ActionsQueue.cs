using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms
{
    public class ActionsQueue : IDisposable
    {
        bool _isDisposing;
        readonly Action<Exception> _onException;
        readonly ExecutionTimeStatsCollector _etscNullable;
        public ActionsQueue(Action<Exception> onException, ExecutionTimeStatsCollector etscNullable)
        {
            if (onException == null) throw new ArgumentNullException(nameof(onException));
            _onException = onException;
            _etscNullable = etscNullable;
        }
        class QueuedAction
        {
            public Action A;
            public string ActionVisibleId;
        }
        readonly Queue<QueuedAction> _queue = new Queue<QueuedAction>(); // locked
        public void Enqueue(Action a, string actionVisibleId) // external thread (receiver thread) // must be very fast lock
        {
            lock (_queue)
            {
                if (_queue.Count > 5000) throw new InsufficientResourcesException();
                _queue.Enqueue(new QueuedAction { A = a, ActionVisibleId = actionVisibleId });
            }
        }
        public int Count
        {
            get
            {
                lock (_queue)
                    return _queue.Count;
            }
        }
        public Task<bool> EnqueueAsync(string actionVisibleId)
        {
            var tcs = new TaskCompletionSource<bool>();
            Enqueue(() =>
            {
                tcs.SetResult(true);
            }, actionVisibleId);
            return tcs.Task;
        }
        public void ExecuteQueued()
        {
            var x = Thread.CurrentThread.ManagedThreadId;
            for (; ; )
            {
                QueuedAction a;
                lock (_queue)
                {
                    a = _queue.Count != 0 ? _queue.Dequeue() : null;
                }
                if (a == null) goto _next;
                if (_isDisposing) return;

                try
                {
                    var sw = Stopwatch.StartNew();
                    a.A();
                    sw.Stop();
                    _etscNullable?.OnMeasuredExecutionTime(a.ActionVisibleId, sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception exc)
                {
                    _onException(exc);
                }
            }

            _next:
            ExecuteDelayedActions();
        }

        public void Dispose()
        {
            _isDisposing = true;
        }


        #region delayed events
        static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private static TimeSpan Time => _stopwatch.Elapsed;
        public void EnqueueDelayed(TimeSpan delay, Action a, string actionVisibleId) // is executed only engine thread
        {
            //if (delay.TotalMinutes > 10)
            //{
            //    Logs.WriteToLog(LogModule.ssk, null, LogLevel.Warning, "adding delayed action '{0}' with timeout '{1}', it can cause memory leak. at {2}",
            //        eventHandler, delay, new StackTrace()
            //        );
            //}

            int eventsSortedByDueTimeIndex = _eventsSortedByDueTimeCounter++ % DelayedActionsSortedByDueTimeArraySize;
            var eventsSortedByDueTime = _delayedActionsSortedByDueTime[eventsSortedByDueTimeIndex]; 
            // select one of linked lists (queues), sequentially.   using of only 1 linked list is slow
            var t = Time + delay;
            var e = new DelayedAction(a, actionVisibleId, t);

            // linkedlist example:  100sec   102sec  104sec   108sec
            //      inserting:           105sec     108sec    109sec     99sec

            // enumerate linkedlist of events starting from end to find an item where to insert
            for (var item = eventsSortedByDueTime.Last; ;)
            {
                if (item == null) break;
                if (item.Value.DueTime < t)
                {
                    eventsSortedByDueTime.AddAfter(item, e);
                    return;
                }

                item = item.Previous;
            }

            eventsSortedByDueTime.AddFirst(e);
        }
        public Task<bool> WaitAsync(TimeSpan delay, string actionVisibleId) // is executed only engine thread
        {
            var tcs = new TaskCompletionSource<bool>();
            EnqueueDelayed(delay, () =>
            {
                tcs.SetResult(true);
            }, actionVisibleId);
            return tcs.Task;
        }
                        
        private class DelayedAction
        {
            public readonly Action EventHandler;
            public readonly string ActionVisibleId;
            public readonly TimeSpan DueTime;
            public DelayedAction(Action eventHandler, string actionVisibleId, TimeSpan dueTime)
            {
                ActionVisibleId = actionVisibleId;
                this.EventHandler = eventHandler;
                this.DueTime = dueTime;
            }
        }

        private const int DelayedActionsSortedByDueTimeArraySize = 128;
        private readonly LinkedList<DelayedAction>[] _delayedActionsSortedByDueTime = CreateEventsSortedByDueTime();
        static LinkedList<DelayedAction>[] CreateEventsSortedByDueTime()
        {
            var r = new LinkedList<DelayedAction>[DelayedActionsSortedByDueTimeArraySize];
            for (int i = 0; i < DelayedActionsSortedByDueTimeArraySize; i++)
                r[i] = new LinkedList<DelayedAction>();
            return r;
        }
        void ExecuteDelayedActions()
        {
            // linkedlist example:  100sec   102sec  104sec   108sec
            //       now:              10sec    102sec

            var now = Time;
            foreach (var eventsSortedByDueTime in _delayedActionsSortedByDueTime)
            {
                for (var item = eventsSortedByDueTime.First; ;)
                {
                    if (item == null) break;
                    var e = item.Value;
                    if (e.DueTime > now) break;

                    // go to next item
                    var itemToRemove = item;
                    item = item.Next;

                    // execute item
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        e.EventHandler();
                        sw.Stop();
                        _etscNullable?.OnMeasuredExecutionTime(e.ActionVisibleId, sw.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception exc)
                    {
                        _onException(exc);
                    }
                    if (_isDisposing) return;

                    // remove executed item
                    eventsSortedByDueTime.Remove(itemToRemove);
                }
            }
        }
        private int _eventsSortedByDueTimeCounter = 0;
        #endregion


    }
}
