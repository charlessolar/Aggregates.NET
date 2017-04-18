using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
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
    class EventMessage : IMessage { }

    class EventSubscriber : IEventSubscriber
    {

        private static readonly ILog Logger = LogManager.GetLogger("EventSubscriber");
        private static readonly Metrics.Timer EventExecution = Metric.Timer("Event Execution", Unit.Items, tags: "debug");
        private static readonly Counter EventsQueued = Metric.Counter("Events Queued", Unit.Items, tags: "debug");
        private static readonly Counter EventsHandled = Metric.Counter("Events Handled", Unit.Items, tags: "debug");
        private static readonly Meter EventErrors = Metric.Meter("Event Failures", Unit.Items);

        private static readonly BlockingCollection<Tuple<string, long, IFullEvent>> WaitingEvents = new BlockingCollection<Tuple<string, long, IFullEvent>>();

        private class ThreadParam
        {
            public IEventStoreConsumer Consumer { get; set; }
            public int Concurrency { get; set; }
            public CancellationToken Token { get; set; }
            public MessageMetadataRegistry MessageMeta { get; set; }
        }


        private Thread _pinnedThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;

        private readonly MessageHandlerRegistry _registry;
        private readonly MessageMetadataRegistry _messageMeta;
        private readonly int _concurrency;
        
        private readonly IEventStoreConsumer _consumer;

        private bool _disposed;


        public EventSubscriber(MessageHandlerRegistry registry, IMessageMapper mapper,
            MessageMetadataRegistry messageMeta, IEventStoreConsumer consumer, int concurrency)
        {
            _registry = registry;
            _consumer = consumer;
            _messageMeta = messageMeta;
            _concurrency = concurrency;
        }

        public async Task Setup(string endpoint, CancellationToken cancelToken)
        {
            _endpoint = endpoint;
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);
            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);

            var discoveredEvents =
                _registry.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).OrderBy(x => x.FullName).ToList();

            if (!discoveredEvents.Any())
            {
                Logger.Warn($"Event consuming is enabled but we did not detect and IEvent handlers");
                return;
            }

            // Dont use - we dont need category projection projecting our projection
            var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}".Replace("-", "");

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

            // Todo: replace with `fromCategories([])` when available
            var appDefinition = string.Format(definition, StreamTypes.Domain, stream, functions);
            var oobDefinition = string.Format(definition, StreamTypes.OOB, stream, functions);
            var pocoDefinition = string.Format(definition, StreamTypes.Poco, stream, functions);

            await _consumer.CreateProjection($"{stream}.app.projection", appDefinition).ConfigureAwait(false);
            await _consumer.CreateProjection($"{stream}.oob.projection", oobDefinition).ConfigureAwait(false);
            await _consumer.CreateProjection($"{stream}.poco.projection", pocoDefinition).ConfigureAwait(false);
        }

        public Task Connect()
        {
            var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";
            var appGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.{StreamTypes.Domain}";
            var oobGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.{StreamTypes.OOB}";
            var pocoGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.{StreamTypes.Poco}";

            Task.Run(async () =>
            {

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    await Task.Delay(1000, _cancelation.Token).ConfigureAwait(false);
                }

                await _consumer.ConnectPinnedPersistentSubscription(stream, appGroup, _cancelation.Token, onEvent, Connect).ConfigureAwait(false);
                await _consumer.ConnectPinnedPersistentSubscription(stream, oobGroup, _cancelation.Token, onEvent, Connect).ConfigureAwait(false);
                await _consumer.ConnectPinnedPersistentSubscription(stream, pocoGroup, _cancelation.Token, onEvent, Connect).ConfigureAwait(false);

                _pinnedThread = new Thread(Threaded)
                { IsBackground = true, Name = $"Main Event Thread" };
                _pinnedThread.Start(new ThreadParam { Token = _cancelation.Token, MessageMeta = _messageMeta, Concurrency = _concurrency, Consumer=_consumer });
                

            });

            return Task.CompletedTask;
        }
        private void onEvent(string stream, long position, IFullEvent e)
        {
            EventsQueued.Increment();
            WaitingEvents.Add(new Tuple<string, long, IFullEvent>(stream, position, e));
        }


        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;


            var semaphore = new SemaphoreSlim(param.Concurrency);
            
            while (true)
            {
                param.Token.ThrowIfCancellationRequested();

                if (semaphore.CurrentCount == 0)
                    Thread.Sleep(100);

                var @event = WaitingEvents.Take(param.Token);

                Task.Run(async () =>
                {
                    await semaphore.WaitAsync(param.Token).ConfigureAwait(false);

                    await
                        ProcessEvent(param.MessageMeta, @event.Item1, @event.Item2, @event.Item3, param.Token)
                            .ConfigureAwait(false);

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Scheduling acknowledge event {@event.Item3.Descriptor.EventId} stream [{@event.Item1}] number {@event.Item2}");
                    await param.Consumer.Acknowledge(@event.Item3).ConfigureAwait(false);

                    semaphore.Release();
                });


                
            }


        }

        // A fake message that will travel through the pipeline in order to process events from the context bag
        private static readonly byte[] Marker= new EventMessage().Serialize(new JsonSerializerSettings()).AsByteArray();

        private static async Task ProcessEvent(MessageMetadataRegistry messageMeta, string stream, long position, IFullEvent @event, CancellationToken token)
        {


            var contextBag = new ContextBag();
            // Hack to get all the events to invoker without NSB deserializing 
            contextBag.Set(Defaults.EventHeader, @event.Event);


            using (var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var processed = false;
                var numberOfDeliveryAttempts = 0;

                while (!processed)
                {
                    var transportTransaction = new TransportTransaction();

                    var messageId = Guid.NewGuid().ToString();
                    var headers = new Dictionary<string, string>(@event.Descriptor.Headers)
                    {
                        [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                        [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(messageMeta, @event.GetType()),
                        [Headers.MessageId] = messageId,
                        [Defaults.EventHeader]="",
                        ["EventId"] = @event.EventId.ToString(),
                        ["EventStream"] = stream,
                        ["EventPosition"] = position.ToString()
                    };

                    using (var ctx = EventExecution.NewContext())
                    {
                        try
                        {
                            // If canceled, this will throw the number of time immediate retry requires to send the message to the error queue
                            token.ThrowIfCancellationRequested();

                            // Don't re-use the event id for the message id
                            var messageContext = new MessageContext(messageId,
                                headers,
                                Marker, transportTransaction, tokenSource,
                                contextBag);
                            await Bus.OnMessage(messageContext).ConfigureAwait(false);
                            EventsHandled.Increment();
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
                                Marker, transportTransaction,
                                numberOfDeliveryAttempts);
                            if (await Bus.OnError(errorContext).ConfigureAwait(false) ==
                                ErrorHandleResult.Handled)
                                break;
                            await Task.Delay((numberOfDeliveryAttempts/5)*200, token).ConfigureAwait(false);
                        }
                    }
                }
            }
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
