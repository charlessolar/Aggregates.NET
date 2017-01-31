using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    class DelayedClient : IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("DelayedClient");
        private static readonly Counter QueuedEvents = Metric.Counter("Waiting Delayed Events", Unit.Events, tags: "debug");

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

        private ConcurrentBag<ResolvedEvent> _waitingEvents;

        // Todo: Change to List<Guid> when/if PR 1143 is published
        private List<ResolvedEvent> _toAck;

        private EventStorePersistentSubscriptionBase _subscription;
        private TimerContext _idleContext;

        public bool Live { get; private set; }
        public string Id => $"{_client.Settings.GossipSeeds[0].EndPoint.Address}.{_stream.Substring(_stream.LastIndexOf(".") + 1)}";

        private bool _disposed;

        public DelayedClient(IEventStoreConnection client, string stream, string group, int maxDelayed, CancellationToken token)
        {
            _client = client;
            _stream = stream;
            _group = group;
            _maxDelayed = maxDelayed;
            _token = token;
            _toAck = new List<ResolvedEvent>();
            _waitingEvents = new ConcurrentBag<ResolvedEvent>();
            


            _acknowledger = Timer.Repeat(state =>
            {
                var info = (DelayedClient)state;

                ResolvedEvent[] toAck = Interlocked.Exchange(ref _toAck, new List<ResolvedEvent>()).ToArray();

                if (!toAck.Any())
                    return Task.CompletedTask;

                if (!info.Live)
                    throw new InvalidOperationException(
                        "Subscription was stopped while events were waiting to be ACKed");

                Acknowledged.Increment(Id, toAck.Length);
                Logger.Write(LogLevel.Debug, () => $"Acknowledging {toAck.Length} events to {Id}");

                var page = 0;
                while (page < toAck.Length)
                {
                    var working = toAck.Skip(page).Take(2000);
                    info._subscription.Acknowledge(working);
                    page += 2000;
                }
                return Task.CompletedTask;
            }, this, TimeSpan.FromSeconds(5), token, "delayed event acknowledger");

        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription.Stop(TimeSpan.FromSeconds(30));
            _acknowledger.Dispose();
        }

        private void EventAppeared(EventStorePersistentSubscriptionBase sub, ResolvedEvent e)
        {
            _token.ThrowIfCancellationRequested();

            Logger.Write(LogLevel.Debug,
                () => $"Delayed event appeared {e.Event.EventId} type {e.Event.EventType} stream [{e.Event.EventStreamId}] number {e.Event.EventNumber} projection event number {e.OriginalEventNumber}");
            Queued.Increment(Id);
            QueuedEvents.Increment(Id);
            _waitingEvents.Add(e);
        }

        private void SubscriptionDropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason, Exception ex)
        {
            Live = false;

            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            // Todo: is it possible to ACK an event from a reconnection?
            if (_toAck.Any())
                throw new InvalidOperationException(
                    $"Eventstore subscription dropped and we need to ACK {_toAck.Count} more events");

            // Need to clear ReadyEvents of events delivered but not processed before disconnect
            Queued.Decrement(Id, _waitingEvents.Count);
            QueuedEvents.Decrement(Id, _waitingEvents.Count);
            Interlocked.Exchange(ref _waitingEvents, new ConcurrentBag<ResolvedEvent>());

            if (reason == SubscriptionDropReason.UserInitiated) return;

            // Run in task.Run because mixing .Wait and async methods is bad bad 
            Task.Run(Connect, _token);
        }
        public async Task Connect()
        {
            Logger.Write(LogLevel.Info,
                () => $"Connecting to subscription group [{_group}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");
            // Todo: play with buffer size?
            _subscription = await _client.ConnectToPersistentSubscriptionAsync(_stream, _group,
                eventAppeared: EventAppeared,
                subscriptionDropped: SubscriptionDropped,
                // Let us accept maxDelayed number of unacknowledged events
                bufferSize: _maxDelayed,
                autoAck: false).ConfigureAwait(false);
            Live = true;
        }

        public void Acknowledge(ResolvedEvent[] events)
        {
            if (!Live)
                throw new InvalidOperationException("Cannot ACK an event, subscription is dead");

            Processed.Increment(Id);
            _idleContext.Dispose();
            _toAck.AddRange(events);
        }

        public ResolvedEvent[] Flush()
        {
            if (!Live) return new ResolvedEvent[] {};
            
            var waiting = Interlocked.Exchange(ref _waitingEvents, new ConcurrentBag<ResolvedEvent>());

            Queued.Decrement(Id, waiting.Count);
            QueuedEvents.Decrement(Id,waiting.Count);
            _idleContext = Idle.NewContext(Id);

            return waiting.ToArray();
        }

    }
}
