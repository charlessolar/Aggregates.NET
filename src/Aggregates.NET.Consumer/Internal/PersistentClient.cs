using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using Metrics;
using Aggregates.Extensions;
using EventStore.ClientAPI.Exceptions;
using NServiceBus.Logging;
using Timer = System.Threading.Timer;

namespace Aggregates.Internal
{
    class PersistentClient : IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("PersistentClient");
        private static readonly Counter QueuedEvents = Metric.Counter("Queued Events", Unit.Events, tags: "debug");

        private static readonly Metrics.Counter Queued = Metric.Counter("Subscription Clients Queued", Unit.Events, tags: "debug");
        private static readonly Metrics.Counter Processed = Metric.Counter("Subscription Clients Processed", Unit.Events, tags: "debug");
        private static readonly Metrics.Counter Acknowledged = Metric.Counter("Subscription Clients Acknowledged", Unit.Events, tags: "debug");
        private static readonly Metrics.Timer Idle = Metric.Timer("Subscription Clients Idle", Unit.None, tags: "debug");

        private readonly IEventStoreConnection _client;
        private readonly string _stream;
        private readonly string _group;
        private readonly int _index;
        private readonly int _readsize;
        private readonly CancellationToken _token;
        private readonly Task _acknowledger;
        private readonly ConcurrentQueue<ResolvedEvent> _waitingEvents;

        private readonly object _ackLock;
        private readonly List<Guid> _toAck;

        private EventStorePersistentSubscriptionBase _subscription;
        private TimerContext _idleContext;

        public bool Live { get; private set; }
        public string Id => $"{_client.Settings.GossipSeeds[0].EndPoint.Address}.{_stream.Substring(_stream.LastIndexOf(".") + 1)}.{_index}";

        private bool _disposed;

        public PersistentClient(IEventStoreConnection client, string stream, string group, int readsize, int index, CancellationToken token)
        {
            _client = client;
            _stream = stream;
            _group = group;
            _index = index;
            _readsize = readsize;
            _token = token;
            _ackLock = new object();
            _toAck = new List<Guid>();
            _waitingEvents = new ConcurrentQueue<ResolvedEvent>();


            _acknowledger = Timer.Repeat(state =>
            {
                var info = (PersistentClient)state;

                Guid[] toAck;
                lock (_ackLock)
                {
                    toAck = _toAck.ToArray();
                    _toAck.Clear();
                }

                if (!toAck.Any())
                    return Task.CompletedTask;

                if (!info.Live)
                    return Task.CompletedTask;
                //throw new InvalidOperationException(
                //    "Subscription was stopped while events were waiting to be ACKed");

                Acknowledged.Increment(Id, toAck.Length);
                Logger.Write(LogLevel.Info, () => $"Acknowledging {toAck.Length} events to {Id}");

                var page = 0;
                while (page < toAck.Length)
                {
                    var working = toAck.Skip(page).Take(2000);
                    info._subscription.Acknowledge(working);
                    page += 2000;
                }
                return Task.CompletedTask;
            }, this, TimeSpan.FromSeconds(30), token, "event acknowledger");

            _client.Connected += _client_Connected;
        }

        private void _client_Connected(object sender, ClientConnectionEventArgs e)
        {
            Task.Run(Connect, _token);
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription.Stop(TimeSpan.FromSeconds(30));
        }

        private void EventAppeared(EventStorePersistentSubscriptionBase sub, ResolvedEvent e)
        {
            _token.ThrowIfCancellationRequested();

            Logger.Write(LogLevel.Debug,
                () => $"Event appeared {e.Event.EventId} type {e.Event.EventType} stream [{e.Event.EventStreamId}] number {e.Event.EventNumber} projection event number {e.OriginalEventNumber}");
            Queued.Increment(Id);
            QueuedEvents.Increment(Id);
            _waitingEvents.Enqueue(e);
        }

        private void SubscriptionDropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason, Exception ex)
        {
            Live = false;

            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            // Todo: is it possible to ACK an event from a reconnection?
            //if (_toAck.Any())
            //    throw new InvalidOperationException(
            //        $"Eventstore subscription dropped and we need to ACK {_toAck.Count} more events");


            // Need to clear ReadyEvents of events delivered but not processed before disconnect
            ResolvedEvent e;
            while (!_waitingEvents.IsEmpty)
            {
                Queued.Decrement(Id);
                QueuedEvents.Decrement(Id);
                _waitingEvents.TryDequeue(out e);
            }
            if (reason == SubscriptionDropReason.UserInitiated) return;

            // Task.Run(Connect, _token);
        }
        public async Task Connect()
        {
            Logger.Write(LogLevel.Info,
                () => $"Connecting to subscription group [{_group}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");
            // Todo: play with buffer size?

            while (!Live)
            {
                await Task.Delay(500, _token).ConfigureAwait(false);

                try
                {
                    _subscription = await _client.ConnectToPersistentSubscriptionAsync(_stream, _group,
                        eventAppeared: EventAppeared,
                        subscriptionDropped: SubscriptionDropped,
                        bufferSize: _readsize * 5,
                        autoAck: false).ConfigureAwait(false);

                    Logger.Write(LogLevel.Info,
                        () => $"Connected to subscription group [{_group}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");
                    Live = true;
                }
                catch (OperationTimedOutException) { }
            }
        }

        public void Acknowledge(ResolvedEvent @event)
        {
            if (!Live)
                throw new InvalidOperationException("Cannot ACK an event, subscription is dead");

            Processed.Increment(Id);
            _idleContext.Dispose();
            lock(_ackLock) _toAck.Add(@event.OriginalEvent.EventId);
        }

        public bool TryDequeue(out ResolvedEvent e)
        {
            e = default(ResolvedEvent);
            if (Live && _waitingEvents.TryDequeue(out e))
            {
                Queued.Decrement(Id);
                QueuedEvents.Decrement(Id);
                _idleContext = Idle.NewContext(Id);
                return true;
            }
            return false;
        }

    }
}
