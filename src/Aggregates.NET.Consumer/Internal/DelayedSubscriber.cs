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
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Transport;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;


namespace Aggregates.Internal
{
    class BulkMessage : IMessage { }

    class DelayedSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger("DelaySubscriber");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");

        private static readonly Metrics.Timer DelayedExecution = Metric.Timer("Delayed Execution", Unit.Items, tags: "debug");
        private static readonly Counter DelayedHandled = Metric.Counter("Delayed Handled", Unit.Items, tags: "debug");
        private static readonly Counter DelayedQueued = Metric.Counter("Delayed Queued", Unit.Items);
        private static readonly Meter DelayedErrors = Metric.Meter("Delayed Failures", Unit.Items);


        private static readonly ConcurrentDictionary<string, List<IFullEvent>> WaitingEvents = new ConcurrentDictionary<string, List<IFullEvent>>();

        private class ThreadParam
        {
            public IEventStoreConsumer Consumer { get; set; }
            public int MaxRetry { get; set; }
            public CancellationToken Token { get; set; }
        }

        private Thread _delayedThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private Version _version;

        private readonly int _maxRetry;

        private readonly IEventStoreConsumer _consumer;

        private bool _disposed;

        public DelayedSubscriber(IEventStoreConsumer consumer, int maxRetry)
        {
            _consumer = consumer;
            _maxRetry = maxRetry;

        }


        public async Task Setup(string endpoint, CancellationToken cancelToken, Version version)
        {
            _endpoint = endpoint;
            _version = version;
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);
            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);

        }
        public async Task Connect()
        {
            var stream = $"$ce-{_endpoint}.{StreamTypes.Delayed}";
            var group = $"{_endpoint}.{_version}.{StreamTypes.Delayed}";

            await Reconnect(stream, group).ConfigureAwait(false);

            _delayedThread = new Thread(Threaded)
            { IsBackground = true, Name = $"Delayed Event Thread" };
            _delayedThread.Start(new ThreadParam { Token = _cancelation.Token, MaxRetry = _maxRetry, Consumer = _consumer });
        }
        private Task Reconnect(string stream, string group)
        {
            return _consumer.ConnectRoundRobinPersistentSubscription(stream, group, _cancelation.Token, onEvent, () => Reconnect(stream, group));
        }

        private void onEvent(string stream, long position, IFullEvent e)
        {
            DelayedQueued.Increment();
            WaitingEvents.AddOrUpdate(stream, (key) => new List<IFullEvent> { e }, (key, existing) =>
              {
                  existing.Add(e);
                  return existing;
              });
        }

        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;
            var random = new Random();

            while (!Bus.BusOnline)
            {
                Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                Thread.Sleep(500);
            }
            try
            {
                while (true)
                {
                    param.Token.ThrowIfCancellationRequested();

                    if (WaitingEvents.Keys.Count == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    List<IFullEvent> flushedEvents;
                    // Pull a random delayed stream for processing
                    if (
                        !WaitingEvents.TryRemove(WaitingEvents.Keys.ElementAt(random.Next(WaitingEvents.Keys.Count)),
                            out flushedEvents))
                        continue;

                    DelayedQueued.Decrement(flushedEvents.Count());

                    try
                    {
                        Task.Run(async () =>
                        {
                            using (var ctx = DelayedExecution.NewContext())
                            {
                                // Same stream ids should modify the same models, processing this way reduces write collisions on commit
                                await ProcessEvents(param, flushedEvents.ToArray()).ConfigureAwait(false);

                                if (ctx.Elapsed > TimeSpan.FromSeconds(5))
                                    SlowLogger.Warn(
                                        $"Processing {flushedEvents.Count()} bulked events took {ctx.Elapsed.TotalSeconds} seconds!");
                                Logger.Write(LogLevel.Info,
                                    () => $"Processing {flushedEvents.Count()} bulked events took {ctx.Elapsed.TotalMilliseconds} ms");

                            }
                        }, param.Token).Wait();
                    }
                    catch (System.AggregateException e)
                    {
                        if (e.InnerException is OperationCanceledException)
                            throw e.InnerException;

                        // If not a canceled exception, just write to log and continue
                        // we dont want some random unknown exception to kill the whole event loop
                        Logger.Error(
                            $"Received exception in main event thread: {e.InnerException.GetType()}: {e.InnerException.Message}", e);
                    }
                }
            }
            catch (OperationCanceledException) { }

        }

        // A fake message that will travel through the pipeline in order to bulk process messages from the context bag
        private static readonly byte[] Marker = new BulkMessage().Serialize(new JsonSerializerSettings()).AsByteArray();

        private static async Task ProcessEvents(ThreadParam param, IFullEvent[] events)
        {

            var delayed = events.Select(x => x.Event as IDelayedMessage).ToArray();

            Logger.Write(LogLevel.Debug, () => $"Processing {delayed.Count()} bulk events from stream [{events.First().Descriptor.StreamId}] bucket [{events.First().Descriptor.Bucket}] entity [{events.First().Descriptor.EntityType}]");

            var contextBag = new ContextBag();
            // Hack to get all the delayed messages to bulk invoker without NSB deserializing and processing each one
            contextBag.Set(Defaults.BulkHeader, delayed);

            // Run bulk process on this thread
            using (var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(param.Token))
            {
                var success = false;
                var retry = 0;
                var messageId = Guid.NewGuid().ToString();
                do
                {
                    var transportTransaction = new TransportTransaction();

                    // Need to supply EnclosedMessageTypes to trick NSB pipeline into processing our fake message
                    var headers = new Dictionary<string, string>()
                    {
                        [Headers.EnclosedMessageTypes] = typeof(BulkMessage).AssemblyQualifiedName,
                        [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                        [Headers.MessageId] = messageId,
                        [Defaults.BulkHeader] = delayed.Count().ToString(),
                    };

                    try
                    {
                        // If canceled, this will throw the number of time immediate retry requires to send the message to the error queue
                        param.Token.ThrowIfCancellationRequested();

                        // Don't re-use the event id for the message id
                        var messageContext = new NServiceBus.Transport.MessageContext(messageId,
                            headers,
                            Marker, transportTransaction, tokenSource,
                            contextBag);
                        await Bus.OnMessage(messageContext).ConfigureAwait(false);//param.Token);

                        tokenSource.Token.ThrowIfCancellationRequested();

                        Logger.Write(LogLevel.Debug,
                            () => $"Scheduling acknowledge of {delayed.Count()} bulk events");
                        DelayedHandled.Increment(delayed.Count());
                        await param.Consumer.Acknowledge(events).ConfigureAwait(false);
                        success = true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // NSB transport has been disconnected
                        break;
                    }
                    catch (Exception e)
                    {
                        // Don't retry a cancelation
                        if (tokenSource.IsCancellationRequested)
                            throw;

                        DelayedErrors.Mark($"{e.GetType().Name} {e.Message}");

                        if ((retry % param.MaxRetry) == 0)
                            Logger.Warn($"So far, we've received {retry} errors while running {delayed.Count()} bulk events from stream [{events.First().Descriptor.StreamId}] bucket [{events.First().Descriptor.Bucket}] entity [{events.First().Descriptor.EntityType}]", e);

                        // Don't burn cpu in case of non-transient errors
                        await Task.Delay((retry / 5) * 200, param.Token).ConfigureAwait(false);
                    }

                    retry++;
                    // Keep retrying forever but print warn messages once MaxRetry exceeded
                } while (!success);

            }
        }


        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancelation?.Cancel();
            _delayedThread?.Join();
        }

    }
}
