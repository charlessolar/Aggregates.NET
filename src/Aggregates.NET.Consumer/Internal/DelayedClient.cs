using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Metrics;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    class DelayedClient : IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("DelayedClient");

        private static readonly Metrics.Counter Queued = Metric.Counter("Delayed Clients Queued", Unit.Events, tags: "debug");
        private static readonly Metrics.Counter Processed = Metric.Counter("Delayed Clients Processed", Unit.Events, tags: "debug");
        private static readonly Metrics.Counter Acknowledged = Metric.Counter("Delayed Clients Acknowledged", Unit.Events, tags: "debug");
        private static readonly Metrics.Timer Idle = Metric.Timer("Delayed Clients Idle", Unit.None, tags: "debug");

        private readonly IEventStoreConnection _client;
        private readonly string _stream;
        private readonly string _group;
        private readonly int _maxDelayed;
        private readonly CancellationToken _token;
        private readonly Task _acknowledger;

        private readonly object _lock;
        private readonly List<ResolvedEvent> _waitingEvents;

        private readonly object _ackLock;
        private readonly List<Guid> _toAck;

        private EventStorePersistentSubscriptionBase _subscription;
        private TimerContext _idleContext;

        public bool Live { get; private set; }
        public string Id => $"{_client.Settings.GossipSeeds[0].EndPoint.Address}.{_stream.Substring(_stream.LastIndexOf(".") + 1)}";

        private bool _disposed;

        public DelayedClient(IEventStoreConnection client, string stream, string group, int maxDelayed,
            CancellationToken token)
        {
            _client = client;
            _stream = stream;
            _group = group;
            _maxDelayed = maxDelayed;
            _token = token;
            _ackLock = new object();
            _toAck = new List<Guid>();
            _lock = new object();
            _waitingEvents = new List<ResolvedEvent>();



            _acknowledger = Timer.Repeat(state =>
            {
                var info = (DelayedClient)state;

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
            }, this, TimeSpan.FromSeconds(30), token, "delayed event acknowledger");

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
                () => $"Delayed event appeared {e.Event.EventId} type {e.Event.EventType} stream [{e.Event.EventStreamId}] number {e.Event.EventNumber} projection event number {e.OriginalEventNumber}");
            Queued.Increment(Id);
            lock(_lock) _waitingEvents.Add(e);
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
            Queued.Decrement(Id, _waitingEvents.Count);
            lock (_lock) _waitingEvents.Clear();

            if (reason == SubscriptionDropReason.UserInitiated) return;

            //Task.Run(Connect, _token);
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
                        // Let us accept maxDelayed number of unacknowledged events
                        bufferSize: _maxDelayed,
                        autoAck: false).ConfigureAwait(false);
                    Live = true;
                    Logger.Write(LogLevel.Info,
                        () => $"Connected to subscription group [{_group}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");
                }
                catch (OperationTimedOutException) { }
            }
        }

        public void Acknowledge(ResolvedEvent[] events)
        {
            Processed.Increment(Id);
            _idleContext.Dispose();
            lock(_ackLock) _toAck.AddRange(events.Select(x => x.OriginalEvent.EventId));
        }

        public void Nack(ResolvedEvent[] events)
        {
            while (!Live)
                Thread.Sleep(10);

            _subscription.Fail(events, PersistentSubscriptionNakEventAction.Retry, "Failed to process");
        }

        public ResolvedEvent[] Flush()
        {
            if (!Live || _waitingEvents.Count == 0) return new ResolvedEvent[] { };

            var age = DateTime.UtcNow - _waitingEvents.First().Event.Created;
            if(_waitingEvents.Count < 100 && age < TimeSpan.FromSeconds(30)) return new ResolvedEvent[] { };

            List<ResolvedEvent> discovered;
            lock (_lock)
            {
                discovered = _waitingEvents.GetRange(0, Math.Min(500, _waitingEvents.Count));
                _waitingEvents.RemoveRange(0, Math.Min(500, _waitingEvents.Count));
            }
            
            Queued.Decrement(Id, discovered.Count);
            _idleContext = Idle.NewContext(Id);

            return discovered.ToArray();
        }

    }
}
