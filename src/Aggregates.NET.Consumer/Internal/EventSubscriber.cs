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
        private static readonly Counter EventsHandled = Metric.Counter("Events Handled", Unit.Items, tags: "debug");
        private static readonly Counter EventsQueued = Metric.Counter("Events Queued", Unit.Items);
        private static readonly Meter EventErrors = Metric.Meter("Event Failures", Unit.Items);

        private readonly BlockingCollection<Tuple<string, long, IFullEvent>>[] _waitingEvents;

        private class ThreadParam
        {
            public IEventStoreConsumer Consumer { get; set; }
            public int Concurrency { get; set; }
            public CancellationToken Token { get; set; }
            public IMessaging Messaging { get; set; }
            public int Index { get; set; }
            public BlockingCollection<Tuple<string, long, IFullEvent>> Queue { get; set; }
        }


        private Thread[] _pinnedThreads;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private Version _version;

        private readonly IMessaging _messaging;
        private readonly int _concurrency;

        private readonly IEventStoreConsumer _consumer;

        private bool _disposed;


        public EventSubscriber(IMessaging messaging, IEventStoreConsumer consumer, int concurrency)
        {
            _messaging = messaging;
            _consumer = consumer;
            _concurrency = concurrency;

            // Initiate multiple event queues, 1 for each concurrent eventstream
            _waitingEvents = new BlockingCollection<Tuple<string, long, IFullEvent>>[_concurrency];
            for (var i = 0; i < _concurrency; i++)
                _waitingEvents[i] = new BlockingCollection<Tuple<string, long, IFullEvent>>();
        }

        public async Task Setup(string endpoint, CancellationToken cancelToken, Version version)
        {
            _endpoint = endpoint;
            // Changes which affect minor version require a new projection, ignore revision and build numbers
            _version = new Version(version.Major, version.Minor);
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);
            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);

            var discoveredEvents =
                _messaging.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).OrderBy(x => x.FullName).ToList();

            if (!discoveredEvents.Any())
            {
                Logger.Warn($"Event consuming is enabled but we did not detect and IEvent handlers");
                return;
            }

            // Dont use - we dont need category projection projecting our projection
            var stream = $"{_endpoint}.{_version}".Replace("-", "");

            // Link all events we are subscribing to to a stream
            var functions =
                discoveredEvents
                    .Select(
                        eventType => $"'{eventType.AssemblyQualifiedName}': processEvent")
                    .Aggregate((cur, next) => $"{cur},\n{next}");

            // Don't tab this '@' will create tabs in projection definition
            var definition = @"
function processEvent(s,e) {{
    linkTo('{1}', e);
}}
fromStreams([{0}]).
when({{
{2}
}});";

            // Todo: replace with `fromCategories([])` when available
            var appDefinition = string.Format(definition, $"'$ce-{StreamTypes.Domain}','$ce-{StreamTypes.OOB}','$ce-{StreamTypes.Poco}'", stream, functions);
            await _consumer.CreateProjection($"{stream}.app.projection", appDefinition).ConfigureAwait(false);
        }

        public async Task Connect()
        {
            // Todo: currently we project the events we want to see and so a single subscription group
            // we can make it so users can specify their own events and subscribe 1 instance to many streams
            // by perhaps putting attributes on events to specify a bounded context and saying '.SubscribeTo("Invoices")'
            var group = $"{_endpoint}.{_version}";
            var appStream = $"{_endpoint}.{_version}";


            _pinnedThreads = new Thread[_concurrency];

            for (var i = 0; i < _concurrency; i++)
            {
                _pinnedThreads[i] = new Thread(Threaded)
                { IsBackground = true, Name = $"Event Thread {i}" };

                _pinnedThreads[i].Start(new ThreadParam { Token = _cancelation.Token, Messaging = _messaging, Concurrency = _concurrency, Consumer = _consumer, Index = i, Queue = _waitingEvents[i] });
            }


            await Reconnect(appStream, group).ConfigureAwait(false);
        }

        private Task Reconnect(string stream, string group)
        {
            return _consumer.ConnectPinnedPersistentSubscription(stream, group, _cancelation.Token, onEvent, () => Reconnect(stream, group));
        }

        private void onEvent(string stream, long position, IFullEvent e)
        {
            EventsQueued.Increment();

            var bucket = Math.Abs(stream.GetHashCode() % _concurrency);
            _waitingEvents[bucket].Add(new Tuple<string, long, IFullEvent>(stream, position, e));
        }


        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;

            while (!Bus.BusOnline)
            {
                Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                Thread.Sleep(1000);
            }

            try
            {
                while (true)
                {
                    param.Token.ThrowIfCancellationRequested();

                    var @event = param.Queue.Take(param.Token);
                    EventsQueued.Decrement();

                    try
                    {
                        ProcessEvent(param.Messaging, @event.Item1, @event.Item2, @event.Item3, param.Token)
                            .ConfigureAwait(false).GetAwaiter().GetResult();

                        Logger.Write(LogLevel.Debug,
                            () =>
                                $"Acknowledge event {@event.Item3.Descriptor.EventId} stream [{@event.Item1}] number {@event.Item2}");
                        param.Consumer.Acknowledge(@event.Item1, @event.Item2, @event.Item3).ConfigureAwait(false)
                            .GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        // If not a canceled exception, just write to log and continue
                        // we dont want some random unknown exception to kill the whole event loop
                        Logger.Error(
                            $"Received exception in main event thread: {e.GetType()}: {e.Message}",
                            e);

                    }
                }
            }
            catch
            {
            }
        }

        // A fake message that will travel through the pipeline in order to process events from the context bag
        private static readonly byte[] Marker = new EventMessage().Serialize(new JsonSerializerSettings()).AsByteArray();

        private static async Task ProcessEvent(IMessaging messaging, string stream, long position, IFullEvent @event, CancellationToken token)
        {
            Logger.Write(LogLevel.Debug, () => $"Processing event from stream [{@event.Descriptor.StreamId}] bucket [{@event.Descriptor.Bucket}] entity [{@event.Descriptor.EntityType}] event id {@event.EventId}");


            var contextBag = new ContextBag();
            // Hack to get all the events to invoker without NSB deserializing 
            contextBag.Set(Defaults.EventHeader, @event.Event);


            using (var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var processed = false;
                var numberOfDeliveryAttempts = 0;

                var messageId = Guid.NewGuid().ToString();
                var corrId = "";
                if (@event.Descriptor?.Headers?.ContainsKey(Headers.CorrelationId) ?? false)
                    corrId = @event.Descriptor.Headers[Headers.CorrelationId];

                while (!processed)
                {
                    var transportTransaction = new TransportTransaction();

                    var headers =
                        new Dictionary<string, string>(@event.Descriptor.Headers ??
                                                       new Dictionary<string, string>())
                        {
                            [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                            [Headers.EnclosedMessageTypes] =
                            SerializeEnclosedMessageTypes(messaging, @event.Event.GetType()),
                            [Headers.MessageId] = messageId,
                            [Headers.CorrelationId] = corrId,
                            [Defaults.EventHeader] = "",
                            [$"{Defaults.EventPrefixHeader}.EventId"] = @event.EventId.ToString(),
                            [$"{Defaults.EventPrefixHeader}.EventStream"] = stream,
                            [$"{Defaults.EventPrefixHeader}.EventPosition"] = position.ToString()
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
                            throw new OperationCanceledException();
                        }
                        catch (Exception ex)
                        {

                            EventErrors.Mark($"{ex.GetType().Name} {ex.Message}");
                            ++numberOfDeliveryAttempts;

                            // Don't retry a cancelation
                            if (tokenSource.IsCancellationRequested)
                                numberOfDeliveryAttempts = Int32.MaxValue;

                            var errorContext = new ErrorContext(ex, headers,
                                messageId,
                                Marker, transportTransaction,
                                numberOfDeliveryAttempts);
                            if (await Bus.OnError(errorContext).ConfigureAwait(false) ==
                                ErrorHandleResult.Handled || tokenSource.IsCancellationRequested)
                                break;
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
            _cancelation?.Cancel();

            if (_pinnedThreads != null)
                Array.ForEach(_pinnedThreads, x => x.Join());
        }

        static string SerializeEnclosedMessageTypes(IMessaging messaging, Type messageType)
        {
            var assemblyQualifiedNames = new HashSet<string>();
            foreach (var type in messaging.GetMessageHierarchy(messageType))
            {
                assemblyQualifiedNames.Add(type.AssemblyQualifiedName);
            }

            return string.Join(";", assemblyQualifiedNames);
        }
    }
}
