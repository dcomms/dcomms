using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms
{
    public class ActionsQueue : IDisposable
    {
        bool _isDisposing;
        readonly Action<Exception> _onException;
        public ActionsQueue(Action<Exception> onException)
        {
            if (onException == null) throw new ArgumentNullException(nameof(onException));
            _onException = onException;
        }
        readonly Queue<Action> _queue = new Queue<Action>(); // locked
        public void Enqueue(Action a) // external thread (receiver thread) // must be very fast lock
        {
            lock (_queue)
            {
                if (_queue.Count > 5000) throw new InsufficientResourcesException();
                _queue.Enqueue(a);
            }
        }
        public Task<bool> EnqueueAsync()
        {
            var tcs = new TaskCompletionSource<bool>();            
            Enqueue(() =>
            {
                tcs.SetResult(true);
            });
            return tcs.Task;

        }
        public void ExecuteQueued()
        {
            for (; ; )
            {
                Action a;
                lock (_queue)
                {
                    a = _queue.Count != 0 ? _queue.Dequeue() : null;
                }
                if (a == null) return;
                if (_isDisposing) return;

                try
                {
                    a();
                }
                catch (Exception exc)
                {
                    _onException(exc);
                }
            }
        }

        public void Dispose()
        {
            _isDisposing = true;
        }
    }
}
