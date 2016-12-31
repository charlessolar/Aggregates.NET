using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.Projections;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Pipeline;
using NServiceBus.Transport;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using MessageContext = NServiceBus.Transport.MessageContext;
using Timer = System.Threading.Timer;

namespace Aggregates.Internal
{
    // Todo: a lot of canceling goes on here because we're dealing with potentially unstable networks
    // Need to lay all this out and make sure we are processing events correctly, even in the case of
    // a single subscription going down amoung many.  No message should be ACKed or discarded without
    // knowing
    internal class EventSubscriber : IEventSubscriber
    {
        private static readonly Counter QueuedEvents = Metric.Counter("Queued Events", Unit.Events);
        private static readonly Histogram Acknowledging = Metric.Histogram("Acknowledged Events", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger("EventSubscriber");

        private class ThreadParam
        {
            public ClientInfo[] Clients { get; set; }
            public CancellationToken Token { get; set; }
            public MessageMetadataRegistry MessageMeta { get; set; }
            public JsonSerializerSettings JsonSettings { get; set; }
        }

        private class ClientInfo : IDisposable
        {
            private readonly IEventStoreConnection _client;
            private readonly string _stream;
            private readonly string _group;
            private readonly CancellationToken _token;
            // Todo: Change to List<Guid> when/if PR 1143 is published
            private readonly List<ResolvedEvent> _toAck;
            private readonly object _ackLock;
            private readonly Timer _acknowledger;
            private readonly ConcurrentQueue<ResolvedEvent> _waitingEvents;

            private EventStorePersistentSubscriptionBase _subscription;

            public bool Live { get; private set; }

            private bool _disposed;

            public ClientInfo(IEventStoreConnection client, string stream, string group, CancellationToken token)
            {
                _client = client;
                _stream = stream;
                _group = group;
                _token = token;
                _toAck = new List<ResolvedEvent>();
                _ackLock = new object();
                _waitingEvents = new ConcurrentQueue<ResolvedEvent>();

                _acknowledger = new Timer(state =>
                {
                    var info = (ClientInfo)state;

                    ResolvedEvent[] toAck;
                    lock (info._ackLock)
                    {
                        toAck = info._toAck.ToArray();
                        info._toAck.Clear();
                    }
                    if (!toAck.Any()) return;

                    if (!info.Live)
                        throw new InvalidOperationException(
                            "Subscription was stopped while events were waiting to be ACKed");


                    Acknowledging.Update(toAck.Length);
                    Logger.Write(LogLevel.Info, () => $"Acknowledging {toAck.Length} events");

                    var page = 0;
                    while (page < toAck.Length)
                    {
                        var working = toAck.Skip(page).Take(2000);
                        info._subscription.Acknowledge(working);
                        page += 2000;
                    }
                }, this, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

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
                    () =>
                            $"Event appeared {e.Event.EventId} type {e.Event.EventType} stream [{e.Event.EventStreamId}] number {e.Event.EventNumber}");
                QueuedEvents.Increment();
                _waitingEvents.Enqueue(e);
            }

            private void SubscriptionDropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason, Exception ex)
            {
                Live = false;

                lock (_ackLock)
                {
                    // Todo: is it possible to ACK an event from a reconnection?
                    if (_toAck.Any())
                        throw new InvalidOperationException(
                            $"Eventstore subscription dropped and we need to ACK {_toAck.Count} more events");
                }
                // Need to clear ReadyEvents of events delivered but not processed before disconnect
                ResolvedEvent e;
                while (!_waitingEvents.IsEmpty)
                {
                    QueuedEvents.Decrement();
                    _waitingEvents.TryDequeue(out e);
                }

                if (reason == SubscriptionDropReason.UserInitiated) return;

                // Restart
                try
                {
                    Connect().Wait(_token);
                }
                catch (OperationCanceledException) { }
            }
            public async Task Connect()
            {
                Logger.Write(LogLevel.Info,
                    () =>
                            $"Connecting to subscription group [{_group}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");
                // Todo: play with buffer size?
                _subscription = await _client.ConnectToPersistentSubscriptionAsync(_stream, _group,
                    eventAppeared: EventAppeared,
                    subscriptionDropped: SubscriptionDropped,
                    bufferSize: 10000,
                    autoAck: false).ConfigureAwait(false);
                Live = true;
            }

            public void Acknowledge(ResolvedEvent @event)
            {
                if (!Live)
                    throw new InvalidOperationException("Cannot ACK an event, subscription is dead");

                lock (_ackLock) _toAck.Add(@event);
            }

            public bool TryDequeue(out ResolvedEvent e)
            {
                e = default(ResolvedEvent);
                if (Live && _waitingEvents.TryDequeue(out e))
                {
                    QueuedEvents.Decrement();
                    return true;
                }
                return false;
            }

        }


        private Thread _pinnedThread;
        private Thread _oobThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private readonly MessageHandlerRegistry _registry;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;

        private readonly IEventStoreConnection[] _clients;

        private bool _disposed;

        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(MessageHandlerRegistry registry, IMessageMapper mapper,
            MessageMetadataRegistry messageMeta, IEventStoreConnection[] connections)
        {
            _registry = registry;
            _clients = connections;
            _messageMeta = messageMeta;
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
        }

        public async Task Setup(string endpoint, int readsize, bool extraStats)
        {
            _endpoint = endpoint;
            _readsize = readsize;
            _extraStats = extraStats;

            foreach (var client in _clients)
            {
                if (client.Settings.GossipSeeds == null || !client.Settings.GossipSeeds.Any())
                    throw new ArgumentException(
                        "Eventstore connection settings does not contain gossip seeds (even if single host call SetGossipSeedEndPoints and SetClusterGossipPort)");

                var manager = new ProjectionsManager(client.Settings.Log,
                    new IPEndPoint(client.Settings.GossipSeeds[0].EndPoint.Address,
                        client.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));

                var discoveredEvents =
                    _registry.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).ToList();

                var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";

                // Link all events we are subscribing to to a stream
                var functions =
                    discoveredEvents
                        .Select(
                            eventType => $"'{eventType.AssemblyQualifiedName}': processEvent")
                        .Aggregate((cur, next) => $"{cur},\n{next}");

                var definition = $@"
function processEvent(s,e) {{
    var stream = e.streamId;
    if(stream.substr(stream.indexOf('.') + 1, 3) === 'OOB')
        linkTo('OOB.{stream}', e);
    else
        linkTo('{stream}', e);
}}
fromAll().when({{
{functions}
}});";

                try
                {
                    var existing = await manager.GetQueryAsync(stream).ConfigureAwait(false);

                    if (existing != definition)
                    {
                        Logger.Fatal(
                            $"Projection [{stream}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!");
                        throw new EndpointVersionException(
                            $"Projection [{stream}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!");
                    }
                }
                catch (ProjectionCommandFailedException)
                {
                    try
                    {
                        // Projection doesn't exist 
                        await
                            manager.CreateContinuousAsync(stream, definition, false,
                                    client.Settings.DefaultUserCredentials)
                                .ConfigureAwait(false);
                    }
                    catch (ProjectionCommandFailedException)
                    {
                    }
                }
            }
        }

        public Task Subscribe(CancellationToken cancelToken)
        {
            var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";
            var pinnedGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.PINNED";
            var roundRobinGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.ROUND";

            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);


            Task.Run(async () =>
            {

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    Thread.Sleep(1000);
                }


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(0)
                    .WithReadBatchOf(_readsize)
                    .WithLiveBufferSizeOf(_readsize * _readsize)
                    .WithMessageTimeoutOf(TimeSpan.MaxValue)
                    .CheckPointAfter(TimeSpan.FromSeconds(2))
                    .MaximumCheckPointCountOf(_readsize * _readsize)
                    .ResolveLinkTos();

                var pinnedClients = new ClientInfo[_clients.Count()];
                var oobClients = new ClientInfo[_clients.Count()];

                for (var i = 0; i < _clients.Count(); i++)
                {
                    var client = _clients.ElementAt(i);

                    var clientCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_cancelation.Token);

                    client.Disconnected += (object s, ClientConnectionEventArgs args) =>
                    {
                        clientCancelSource.Cancel();
                    };

                    try
                    {
                        settings.WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);
                        await
                            client.CreatePersistentSubscriptionAsync(stream, pinnedGroup, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created PINNED persistent subscription to stream [{stream}]");

                    }
                    catch (InvalidOperationException)
                    {
                    }
                    try
                    {

                        settings.WithNamedConsumerStrategy(SystemConsumerStrategies.RoundRobin);
                        await
                            client.CreatePersistentSubscriptionAsync($"OOB.{stream}", roundRobinGroup, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created ROUND ROBIN persistent subscription to stream [{stream}]");
                    }
                    catch (InvalidOperationException)
                    {
                    }



                    pinnedClients[i] = new ClientInfo(client, stream, pinnedGroup, clientCancelSource.Token);
                    oobClients[i] = new ClientInfo(client, $"OOB.{stream}", roundRobinGroup, clientCancelSource.Token);


                }

                _pinnedThread = new Thread(Threaded)
                { IsBackground = true, Name = $"Pinned Event Thread" };
                _pinnedThread.Start(new ThreadParam { Token = cancelToken, Clients = pinnedClients, MessageMeta = _messageMeta, JsonSettings = _settings });

                _oobThread = new Thread(Threaded)
                { IsBackground = true, Name = $"OOB Event Thread" };
                _oobThread.Start(new ThreadParam { Token = cancelToken, Clients = oobClients, MessageMeta = _messageMeta, JsonSettings = _settings });

            });

            return Task.CompletedTask;
        }

        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;

            Task.WhenAll(param.Clients.Select(x => x.Connect())).Wait();

            while (true)
            {
                param.Token.ThrowIfCancellationRequested();

                var foundOne = false;
                var tasks = param.Clients.Select(async x =>
                {
                    ResolvedEvent e;
                    if (!x.TryDequeue(out e))
                        return;

                    var @event = e.Event;

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Processing event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");

                    if (!@event.IsJson)
                        return;

                    foundOne = true;
                    try
                    {
                        await Task.Run(() => ProcessEvent(param.MessageMeta, param.JsonSettings, @event, param.Token), param.Token).ConfigureAwait(false);
                        x.Acknowledge(e);
                    }
                    catch (OperationCanceledException) { }
                });
                // Give events a max of 100ms to complete, then move on to the next round
                // (they'll continue executing this just prevents a long event from slowing everyone down)
                var timeout = Task.Delay(100);
                Task.WhenAny(timeout, Task.WhenAll(tasks)).Wait(param.Token);

                // Cheap hack to not burn cpu incase there are no events
                if (!foundOne)
                    Thread.Sleep(10);
            }


        }

        private static async Task ProcessEvent(MessageMetadataRegistry messageMeta, JsonSerializerSettings settings, RecordedEvent @event, CancellationToken token)
        {
            var transportTransaction = new TransportTransaction();
            var contextBag = new ContextBag();

            var descriptor = @event.Metadata.Deserialize(settings);

            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>(descriptor.Headers)
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(messageMeta, Type.GetType(@event.EventType)),
                [Headers.MessageId] = messageId,
                ["EventId"] = @event.EventId.ToString()
            };

            using (var tokenSource = new CancellationTokenSource())
            {
                var processed = false;
                var numberOfDeliveryAttempts = 0;

                while (!processed)
                {
                    try
                    {
                        // If canceled, this will throw the number of time immediate retry requires to send the message to the error queue
                        token.ThrowIfCancellationRequested();

                        // Don't re-use the event id for the message id
                        var messageContext = new MessageContext(messageId,
                            headers,
                            @event.Data ?? new byte[0], transportTransaction, tokenSource,
                            contextBag);
                        await Bus.OnMessage(messageContext).ConfigureAwait(false);
                        processed = true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // NSB transport has been disconnected
                        break;
                    }
                    catch (Exception ex)
                    {
                        ++numberOfDeliveryAttempts;
                        var errorContext = new ErrorContext(ex, headers,
                            messageId,
                            @event.Data ?? new byte[0], transportTransaction,
                            numberOfDeliveryAttempts);
                        if (await Bus.OnError(errorContext).ConfigureAwait(false) ==
                            ErrorHandleResult.Handled)
                            break;
                    }
                }
            }
        }

        // Moved event to error queue
        private static async Task PoisonEvent(MessageMetadataRegistry messageMeta, JsonSerializerSettings settings, RecordedEvent e)
        {
            var transportTransaction = new TransportTransaction();
            var contextBag = new ContextBag();

            var descriptor = e.Metadata.Deserialize(settings);

            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>(descriptor.Headers)
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(messageMeta, Type.GetType(e.EventType)),
                [Headers.MessageId] = messageId,
                ["EventId"] = e.EventId.ToString()
            };
            var ex = new InvalidOperationException("Poisoned Event");
            var errorContext = new ErrorContext(ex, headers,
                            messageId,
                            e.Data ?? new byte[0], transportTransaction,
                            int.MaxValue);
            await Bus.OnError(errorContext).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancelation.Cancel();
            _pinnedThread.Join();
            _oobThread.Join();
        }

        static string SerializeEnclosedMessageTypes(MessageMetadataRegistry messageMeta, Type messageType)
        {
            var metadata = messageMeta.GetMessageMetadata(messageType);

            var assemblyQualifiedNames = new HashSet<string>();
            foreach (var type in metadata.MessageHierarchy)
            {
                assemblyQualifiedNames.Add(type.AssemblyQualifiedName);
            }

            return string.Join(";", assemblyQualifiedNames);
        }
    }
}
