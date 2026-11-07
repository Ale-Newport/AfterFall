using System;
using System.Collections.Generic;

namespace Afterfall.Core
{
    /// <summary>Marker for typed simulation events (see Afterfall.Domain.Events).</summary>
    public interface IGameEvent
    {
    }

    /// <summary>
    /// Typed publish/subscribe channel between the simulation and its observers
    /// (presentation, audio, diagnostics). Subscriptions are per event type, every
    /// publication is recorded in a bounded journal for debugging, and handlers are
    /// isolated so one failing observer cannot break the simulation.
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();
        private readonly Queue<string> _journal = new Queue<string>();
        private readonly int _journalCapacity;

        public EventBus(int journalCapacity = 256)
        {
            _journalCapacity = journalCapacity;
        }

        /// <summary>Raised when a handler throws. The exception is not propagated.</summary>
        public event Action<IGameEvent, Exception> HandlerFailed;

        public long PublishedCount { get; private set; }

        public IEnumerable<string> Journal => _journal;

        public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Type key = typeof(T);
            _handlers.TryGetValue(key, out Delegate existing);
            _handlers[key] = Delegate.Combine(existing, handler);
            return new Subscription(() => Unsubscribe(handler));
        }

        public void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
        {
            Type key = typeof(T);
            if (!_handlers.TryGetValue(key, out Delegate existing)) return;
            Delegate remaining = Delegate.Remove(existing, handler);
            if (remaining == null) _handlers.Remove(key);
            else _handlers[key] = remaining;
        }

        public void Publish<T>(T evt) where T : IGameEvent
        {
            PublishedCount++;
            _journal.Enqueue(evt.ToString());
            while (_journal.Count > _journalCapacity) _journal.Dequeue();

            if (!_handlers.TryGetValue(typeof(T), out Delegate del)) return;
            // Delegates are immutable: iterating a snapshot is safe even if handlers (un)subscribe.
            foreach (Delegate d in del.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(evt);
                }
                catch (Exception ex)
                {
                    HandlerFailed?.Invoke(evt, ex);
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}
