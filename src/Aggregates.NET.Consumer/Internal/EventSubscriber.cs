using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
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

namespace Aggregates.Internal
{
    // Todo: a lot of canceling goes on here because we're dealing with potentially unstable networks
    // Need to lay all this out and make sure we are processing events correctly, even in the case of
    // a single subscription going down amoung many.  No message should be ACKed or discarded without
    // knowing
    class EventSubscriber : IEventSubscriber
    {

        private static readonly ILog Logger = LogManager.GetLogger("EventSubscriber");
        private static readonly Metrics.Timer EventExecution = Metric.Timer("Event Execution", Unit.Items, tags: "debug");
        private static readonly Counter EventCount = Metric.Counter("Event Messages", Unit.Items);
        private static readonly Meter EventErrors = Metric.Meter("Event Failures", Unit.Items);

        private class ThreadParam
        {
            public PersistentClient[] Clients { get; set; }
            public CancellationToken Token { get; set; }
            public MessageMetadataRegistry MessageMeta { get; set; }
            public JsonSerializerSettings JsonSettings { get; set; }
        }


        private Thread _pinnedThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private readonly MessageHandlerRegistry _registry;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;
        private readonly int _concurrency;

        private readonly IEventStoreConnection[] _clients;

        private bool _disposed;


        public EventSubscriber(MessageHandlerRegistry registry, IMessageMapper mapper,
            MessageMetadataRegistry messageMeta, IEventStoreConnection[] connections, int concurrency)
        {
            _registry = registry;
            _clients = connections;
            _messageMeta = messageMeta;
            _concurrency = concurrency;
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new EventSerializationBinder(mapper),
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

                await manager.EnableAsync("$by_category", client.Settings.DefaultUserCredentials).ConfigureAwait(false);

                var discoveredEvents =
                    _registry.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).ToList();

                // Dont use - we dont need category projection projecting our projection
                var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}".Replace("-","");

                // Link all events we are subscribing to to a stream
                var functions =
                    discoveredEvents
                        .Select(
                            eventType => $"'{eventType.AssemblyQualifiedName}': processEvent")
                        .Aggregate((cur, next) => $"{cur},\n{next}");

                // Don't tab this '@' will create tabs in projection definition
                var definition = @"
function processEvent(s,e) {{
    linkTo('{1}.{0}', e);
}}
fromCategory('{0}').
when({{
{2}
}});";

                var appDefinition = string.Format(definition, StreamTypes.Domain, stream, functions).Replace(Environment.NewLine, "\n");
                var oobDefinition = string.Format(definition, StreamTypes.OOB, stream, functions).Replace(Environment.NewLine, "\n");

                // Create a projection for domain events and one for OOB events, later we'll subscribe as PINNED to domain events
                // and ROUNDROBIN for OOB events.  
                // OOB events by definition don't need ordering, so theres no reason to overload a single PINNED consumer
                // if the user uses a ton of OOB events
                try
                {
                    var existing = await manager.GetQueryAsync($"{stream}.app.projection").ConfigureAwait(false);
                    
                    if (existing != appDefinition)
                    {
                        Logger.Fatal(
                            $"Projection [{stream}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!\nExisting:\n{existing}\nDesired:\n{appDefinition}");
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
                            manager.CreateContinuousAsync($"{stream}.app.projection", appDefinition, false,
                                    client.Settings.DefaultUserCredentials)
                                .ConfigureAwait(false);
                    }
                    catch (ProjectionCommandFailedException)
                    {
                    }
                }
                try
                {
                    var existing = await manager.GetQueryAsync($"{stream}.oob.projection").ConfigureAwait(false);

                    if (existing != oobDefinition)
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
                            manager.CreateContinuousAsync($"{stream}.oob.projection", oobDefinition, false,
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
            var roundRobbinGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.ROUND";
            var pinnedGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.PINNED";

            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);


            Task.Run(async () =>
            {

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    await Task.Delay(1000, cancelToken).ConfigureAwait(false);
                }


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(10)
                    .WithReadBatchOf(_readsize)
                    .WithLiveBufferSizeOf(_readsize * _readsize)
                    //.WithMessageTimeoutOf(TimeSpan.FromMilliseconds(int.MaxValue))
                    .WithMessageTimeoutOf(TimeSpan.FromMinutes(1))
                    .CheckPointAfter(TimeSpan.FromSeconds(5))
                    .MaximumCheckPointCountOf(_readsize * _readsize)
                    .ResolveLinkTos();
                if (_extraStats)
                    settings.WithExtraStatistics();

                var clients = new PersistentClient[_clients.Count() * _concurrency * 2];

                for (var i = 0; i < _clients.Count(); i++)
                {
                    var client = _clients.ElementAt(i);

                    var clientCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_cancelation.Token);

                    client.Closed += (object s, ClientClosedEventArgs args) =>
                    {
                        Logger.Info($"Eventstore disconnected - shutting down client subscription");
                        clientCancelSource.Cancel();
                    };

                    try
                    {
                        settings.WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);
                        await
                            client.CreatePersistentSubscriptionAsync($"{stream}.{StreamTypes.Domain}", pinnedGroup, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created PINNED persistent subscription to stream [{stream}]");

                    }
                    catch (InvalidOperationException)
                    {
                    }
                    try
                    {
                        settings.WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);
                        await
                            client.CreatePersistentSubscriptionAsync($"{stream}.{StreamTypes.OOB}", roundRobbinGroup, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created ROUND ROBIN persistent subscription to stream [{stream}]");

                    }
                    catch (InvalidOperationException)
                    {
                    }

                    for (var j = 0; j < _concurrency; j += 1)
                    {
                        // If multiple clients lay out clients array like so:  (assuming 2 clients with 4 concurrency)
                        //
                        //  ij
                        //  00 00 01 01 02 02 03 03
                        //  10 10 11 11 12 12 13 13
                        //
                        clients[2 * ((i * _concurrency) + j)] = new PersistentClient(client, $"{stream}.{StreamTypes.Domain}", pinnedGroup, _readsize, j, clientCancelSource.Token);
                        clients[2 * ((i * _concurrency) + j) + 1] = new PersistentClient(client, $"{stream}.{StreamTypes.OOB}", roundRobbinGroup, _readsize, j, clientCancelSource.Token);
                    }
                }

                _pinnedThread = new Thread(Threaded)
                { IsBackground = true, Name = $"Main Event Thread" };
                _pinnedThread.Start(new ThreadParam { Token = cancelToken, Clients = clients, MessageMeta = _messageMeta, JsonSettings = _settings });

            });

            return Task.CompletedTask;
        }
       

        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;

            param.Clients.SelectAsync(x => x.Connect()).Wait();

            var threadIdle = Metric.Timer("Process Events Idle", Unit.None, tags: "debug");
            var tasks = new Task[param.Clients.Count()];

            TimerContext? idleContext = threadIdle.NewContext();
            while (true)
            {
                param.Token.ThrowIfCancellationRequested();

                var noEvents = true;
                for (var i = 0; i < param.Clients.Count(); i++)
                {
                    // Check if current task is complete
                    if (tasks[i] != null && !tasks[i].IsCompleted)
                        continue;

                    // Ready for a new event
                    var client = param.Clients.ElementAt(i);
                    
                    ResolvedEvent e;
                    if (!client.TryDequeue(out e))
                        continue;

                    var @event = e.Event;

                    Logger.Write(LogLevel.Debug,
                        () => $"Processing event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");

                    noEvents = false;

                    idleContext?.Dispose();
                    idleContext = null;

                    tasks[i] = Task.Run(async () =>
                    {
                        await ProcessEvent(param.MessageMeta, param.JsonSettings, @event, param.Token).ConfigureAwait(false);

                        Logger.Write(LogLevel.Debug,
                            () => $"Scheduling acknowledge event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");
                        client.Acknowledge(e);
                    }, param.Token);
                }
                if (idleContext == null)
                    idleContext = threadIdle.NewContext();
                // Cheap hack to not burn cpu incase there are no events
                if (noEvents) Thread.Sleep(50);
            }


        }

        private static async Task ProcessEvent(MessageMetadataRegistry messageMeta, JsonSerializerSettings settings, RecordedEvent @event, CancellationToken token)
        {
            var transportTransaction = new TransportTransaction();
            var contextBag = new ContextBag();

            var metadata = @event.Metadata;
            var data = @event.Data;

            var descriptor = metadata.Deserialize(settings);

            if (descriptor.Compressed)
                data = data.Decompress();

            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>(descriptor.Headers)
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(messageMeta, Type.GetType(@event.EventType)),
                [Headers.MessageId] = messageId,
                ["EventId"] = @event.EventId.ToString(),
                ["EventStreamId"] = @event.EventStreamId,
                ["EventNumber"] = @event.EventNumber.ToString()
            };

            using (var tokenSource = new CancellationTokenSource())
            {
                var processed = false;
                var numberOfDeliveryAttempts = 0;

                while (!processed)
                {
                    using (var ctx = EventExecution.NewContext())
                    {
                        try
                        {
                            // If canceled, this will throw the number of time immediate retry requires to send the message to the error queue
                            token.ThrowIfCancellationRequested();

                            // Don't re-use the event id for the message id
                            var messageContext = new MessageContext(messageId,
                                headers,
                                data ?? new byte[0], transportTransaction, tokenSource,
                                contextBag);
                            await Bus.OnMessage(messageContext).ConfigureAwait(false);
                            EventCount.Increment();
                            processed = true;
                        }
                        catch (ObjectDisposedException)
                        {
                            // NSB transport has been disconnected
                            break;
                        }
                        catch (Exception ex)
                        {
                            EventErrors.Mark($"{ex.GetType().Name} {ex.Message}");
                            ++numberOfDeliveryAttempts;
                            var errorContext = new ErrorContext(ex, headers,
                                messageId,
                                data ?? new byte[0], transportTransaction,
                                numberOfDeliveryAttempts);
                            if (await Bus.OnError(errorContext).ConfigureAwait(false) ==
                                ErrorHandleResult.Handled)
                                break;

                        }
                    }
                }
            }
        }

        // Moved event to error queue
        private static async Task PoisonEvent(MessageMetadataRegistry messageMeta, JsonSerializerSettings settings, RecordedEvent e)
        {
            var transportTransaction = new TransportTransaction();

            var descriptor = e.Metadata.Deserialize(settings);

            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>(descriptor.Headers)
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(messageMeta, Type.GetType(e.EventType)),
                [Headers.MessageId] = messageId,
                ["EventId"] = e.EventId.ToString(),
                ["EventStreamId"] = e.EventStreamId,
                ["EventNumber"] = e.EventNumber.ToString()
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
