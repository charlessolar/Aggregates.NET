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
using Aggregates.Messages;
using Microsoft.Extensions.Logging;

namespace Aggregates.Internal
{
    public class DelayedSubscriber : IEventSubscriber
    {
        private readonly ILogger Logger;
        private readonly ILogger SlowLogger;

        private static readonly ConcurrentDictionary<string, LinkedList<Tuple<long, IFullEvent>>> WaitingEvents = new ConcurrentDictionary<string, LinkedList<Tuple<long, IFullEvent>>>();

        private class ThreadParam
        {
            public ILogger Logger { get; set; }
            public IContainer Container { get; set; }
            public int MaxRetry { get; set; }
            public CancellationToken Token { get; set; }
        }

        private Thread _delayedThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private Version _version;

        private readonly int _maxRetry;

        private readonly Configure _settings;
        private readonly IEventStoreConsumer _consumer;
        private readonly IMetrics _metrics;
        private readonly IMessageDispatcher _dispatcher;

        private bool _disposed;

        public DelayedSubscriber(ILoggerFactory logFactory, Configure settings, IMetrics metrics, IEventStoreConsumer consumer, IMessageDispatcher dispatcher, int maxRetry)
        {
            Logger = logFactory.CreateLogger("DelaySubscriber");
            SlowLogger = logFactory.CreateLogger("Slow Alarm");
            _settings = settings;
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
            _delayedThread.Start(new ThreadParam { Logger = Logger, Container = _settings.Container, Token = _cancelation.Token, MaxRetry = _maxRetry });
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

        private Task onEvent(string stream, long position, IFullEvent e)
        {
            _metrics.Increment("Delayed Queued", Unit.Event);
            WaitingEvents.AddOrUpdate(stream, (key) => new LinkedList<Tuple<long, IFullEvent>>(new[] { new Tuple<long, IFullEvent>(position, e) }.AsEnumerable()), (key, existing) =>
               {
                   existing.AddLast(new Tuple<long, IFullEvent>(position, e));
                   return existing;
               });
            return Task.CompletedTask;
        }

        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;

            var container = param.Container;

            var logger = param.Logger;
            var metrics = container.Resolve<IMetrics>();
            var consumer = container.Resolve<IEventStoreConsumer>();
            var dispatcher = container.Resolve<IMessageDispatcher>();
            var logFactory = container.Resolve<ILoggerFactory>();

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

                    LinkedList<Tuple<long, IFullEvent>> flushedEvents;
                    string stream = "";
                    // Pull a random delayed stream for processing
                    try
                    {
                        stream = WaitingEvents.Keys.ElementAt(random.Next(WaitingEvents.Keys.Count));
                        if (!WaitingEvents.TryRemove(stream, out flushedEvents))
                            continue;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Keys.Count is not thread safe and this can throw very occasionally
                        continue;
                    }


                    try
                    {
                        Task.Run(async () =>
                        {
                            logger.DebugEvent("Processing", "{Messages} bulk events", flushedEvents.Count);

                            metrics.Decrement("Delayed Queued", Unit.Event, flushedEvents.Count(x => x.Item2.Event != null));

                            var messages = flushedEvents.Where(x => x.Item2.Event != null).Select(x =>
                            {
                                // Unpack the delayed message for delivery - the IDelayedMessage wrapper should be transparent to other services
                                var delayed = x.Item2.Event as IDelayedMessage;

                                var headers = delayed.Headers.Merge(x.Item2.Descriptor.Headers);
                                headers[Defaults.ChannelKey] = delayed.ChannelKey;

                                return new FullMessage
                                {
                                    Message = delayed.Message,
                                    Headers = headers
                                };
                            });


                            // Same stream ids should modify the same models, processing this way reduces write collisions on commit
                            await dispatcher.SendLocal(messages.ToArray()).ConfigureAwait(false);
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
                        logger.ErrorEvent("Exception", e, "From event thread: {ExceptionType} - {ExceptionMessage}", e.GetType().Name, e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException))
                    logger.ErrorEvent("Died", e, "Event thread closed: {ExceptionType} - {ExceptionMessage}", e.GetType().Name, e.Message);
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
