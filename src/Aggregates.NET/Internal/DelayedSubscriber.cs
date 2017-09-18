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
using Aggregates.Logging;
using Aggregates.Messages;


namespace Aggregates.Internal
{
    class BulkMessage : IMessage { }

    class DelayedSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogProvider.GetLogger("DelaySubscriber");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");
        
        private static readonly ConcurrentDictionary<string, List<Tuple<long, IFullEvent>>> WaitingEvents = new ConcurrentDictionary<string, List<Tuple<long, IFullEvent>>>();

        private class ThreadParam
        {
            public int MaxRetry { get; set; }
            public CancellationToken Token { get; set; }
        }

        private Thread _delayedThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private Version _version;

        private readonly int _maxRetry;

        private readonly IEventStoreConsumer _consumer;
        private readonly IMetrics _metrics;
        private readonly IMessageDispatcher _dispatcher;

        private bool _disposed;

        public DelayedSubscriber(IMetrics metrics, IEventStoreConsumer consumer, IMessageDispatcher dispatcher, int maxRetry)
        {
            _metrics = metrics;
            _consumer = consumer;
            _dispatcher = dispatcher;
            _maxRetry = maxRetry;
        }


        public async Task Setup(string endpoint, Version version)
        {
            _endpoint = endpoint;
            // Changes which affect minor version require a new projection, ignore revision and build numbers
            _version = new Version(version.Major, version.Minor);
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);
            _cancelation = new CancellationTokenSource();

        }
        public async Task Connect()
        {
            var stream = $"$ce-{_endpoint}.{StreamTypes.Delayed}";
            var group = $"{_endpoint}.{_version}.{StreamTypes.Delayed}";

            await Reconnect(stream, group).ConfigureAwait(false);

            _delayedThread = new Thread(Threaded)
            { IsBackground = true, Name = $"Delayed Event Thread" };
            _delayedThread.Start(new ThreadParam { Token = _cancelation.Token, MaxRetry = _maxRetry });
        }
        public Task Shutdown()
        {
            _cancelation.Cancel();
            _delayedThread.Join();

            return Task.CompletedTask;
        }

        private Task Reconnect(string stream, string group)
        {
            return _consumer.ConnectRoundRobinPersistentSubscription(stream, group, _cancelation.Token, onEvent, () => Reconnect(stream, group));
        }

        private void onEvent(string stream, long position, IFullEvent e)
        {
            _metrics.Increment("Delayed Queued", Unit.Event);
            WaitingEvents.AddOrUpdate(stream, (key) => new List<Tuple<long, IFullEvent>> { new Tuple<long, IFullEvent>(position, e)}, (key, existing) =>
              {
                  existing.Add(new Tuple<long, IFullEvent>(position, e));
                  return existing;
              });
        }

        private static void Threaded(object state)
        {
            var container = Configuration.Settings.Container;

            var metrics = container.Resolve<IMetrics>();
            var consumer = container.Resolve<IEventStoreConsumer>();
            var dispatcher = container.Resolve<IMessageDispatcher>();
            
            var param = (ThreadParam)state;
            var random = new Random();
            
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

                    List<Tuple<long, IFullEvent>> flushedEvents;
                    string stream = "";
                    // Pull a random delayed stream for processing
                    try
                    {
                        stream = WaitingEvents.Keys.ElementAt(random.Next(WaitingEvents.Keys.Count));
                        if (
                            !WaitingEvents.TryRemove(stream,
                                out flushedEvents))
                            continue;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Keys.Count is not thread safe and this can throw very occasionally
                        continue;
                    }

                    metrics.Decrement("Delayed Queued", Unit.Event);

                    try
                    {
                        Task.Run(async () =>
                        {
                            Logger.Write(LogLevel.Info,
                                () => $"Processing {flushedEvents.Count()} bulked events");


                            var messages = flushedEvents.Select(x => new FullMessage
                            {
                                Message = x.Item2.Event,
                                Headers = x.Item2.Descriptor.Headers
                            });

                            // Same stream ids should modify the same models, processing this way reduces write collisions on commit
                            await dispatcher.SendLocal(messages.ToArray()).ConfigureAwait(false);
                            
                            Logger.Write(LogLevel.Info,
                                () => $"Finished processing {flushedEvents.Count()} bulked events");
                            foreach (var @event in flushedEvents)
                                    await consumer.Acknowledge(stream, @event.Item1, @event.Item2)
                                        .ConfigureAwait(false);
                            
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

        //// A fake message that will travel through the pipeline in order to bulk process messages from the context bag
        //private static readonly byte[] Marker = new BulkMessage().Serialize(new JsonSerializerSettings()).AsByteArray();

        //private static async Task ProcessEvents(ThreadParam state, IFullEvent[] events)
        //{
        //    var param = (ThreadParam)state;
            


        //    var delayed = events.Select(x => x.Event as IDelayedMessage).ToArray();

        //    Logger.Write(LogLevel.Debug, () => $"Processing {delayed.Count()} bulk events from stream [{events.First().Descriptor.StreamId}] bucket [{events.First().Descriptor.Bucket}] entity [{events.First().Descriptor.EntityType}]");

        //    var contextBag = new ContextBag();
        //    // Hack to get all the delayed messages to bulk invoker without NSB deserializing and processing each one
        //    contextBag.Set(Defaults.BulkHeader, delayed);

        //    // Run bulk process on this thread
        //    using (var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(param.Token))
        //    {
        //        var success = false;
        //        var retry = 0;
        //        var messageId = Guid.NewGuid().ToString();
        //        do
        //        {
        //            var transportTransaction = new TransportTransaction();

        //            // Need to supply EnclosedMessageTypes to trick NSB pipeline into processing our fake message
        //            var headers = new Dictionary<string, string>()
        //            {
        //                [Headers.EnclosedMessageTypes] = typeof(BulkMessage).AssemblyQualifiedName,
        //                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
        //                [Headers.MessageId] = messageId,
        //                [Defaults.BulkHeader] = delayed.Count().ToString(),
        //            };

        //            try
        //            {
        //                // If canceled, this will throw the number of time immediate retry requires to send the message to the error queue
        //                param.Token.ThrowIfCancellationRequested();

        //                // Don't re-use the event id for the message id
        //                var messageContext = new NServiceBus.Transport.MessageContext(messageId,
        //                    headers,
        //                    Marker, transportTransaction, tokenSource,
        //                    contextBag);
        //                await Bus.OnMessage(messageContext).ConfigureAwait(false);//param.Token);

        //                tokenSource.Token.ThrowIfCancellationRequested();

        //                Logger.Write(LogLevel.Debug,
        //                    () => $"Processed {delayed.Count()} bulk events");
        //                DelayedHandled.Increment(delayed.Count());

        //                Defaults.MinimumLogging.Value = null;
        //                success = true;
        //            }
        //            catch (ObjectDisposedException)
        //            {
        //                // NSB transport has been disconnected
        //                throw new OperationCanceledException();
        //            }
        //            catch (Exception e)
        //            {
        //                // Don't retry a cancelation
        //                if (tokenSource.IsCancellationRequested)
        //                    throw;

        //                DelayedErrors.Mark($"{e.GetType().Name} {e.Message}");

        //                if ((retry % param.MaxRetry) == 0 && retry < 100)
        //                {
        //                    Logger.Warn(
        //                        $"So far, we've received {retry} errors while running {delayed.Count()} bulk events from stream [{events.First().Descriptor.StreamId}] bucket [{events.First().Descriptor.Bucket}] entity [{events.First().Descriptor.EntityType}]",
        //                        e);

        //                    Defaults.MinimumLogging.Value = LogLevel.Debug;
        //                    Logger.Info(
        //                        $"Switching to verbose logging, {retry} errors detected while processing {delayed.Count()} bulk events from stream [{events.First().Descriptor.StreamId}] bucket [{events.First().Descriptor.Bucket}] entity [{events.First().Descriptor.EntityType}]");
        //                }
        //                else if (retry > 100)
        //                {
        //                    Logger.Error(
        //                        $"Failed to process delayed events from stream [{events.First().Descriptor.StreamId}] bucket [{events.First().Descriptor.Bucket}] entity [{events.First().Descriptor.EntityType}]",
        //                        e);
        //                    break;
        //                }
        //                // Don't burn cpu in case of non-transient errors
        //                await Task.Delay((retry / 5) * 200, param.Token).ConfigureAwait(false);
        //            }

        //            retry++;
        //            // Keep retrying forever but print warn messages once MaxRetry exceeded
        //        } while (!success);

        //    }
        //}


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
