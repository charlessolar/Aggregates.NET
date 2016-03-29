using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Unicast;
using NServiceBus.Settings;
using NServiceBus.Logging;
using Aggregates.Exceptions;
using Aggregates.Attributes;
using NServiceBus.MessageInterfaces;
using System.Collections.Concurrent;
using Metrics;
using Aggregates.Contracts;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Threading;
using Aggregates.Extensions;

namespace Aggregates.Internal
{
    public class NServiceBusDispatcher : IDispatcher, IDisposable
    {
        private class Job
        {
            public Object Event { get; set; }
            public IEventDescriptor Descriptor { get; set; }
            public long? Position { get; set; }
        }
        private class DelayedJob
        {
            public Object Event { get; set; }
            public IEventDescriptor Descriptor { get; set; }
            public long? Position { get; set; }
            public int Retry { get; set; }
            public long FailedAt { get; set; }
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;

        private readonly ConcurrentDictionary<String, IList<Type>> _invokeCache;
        private readonly ParallelOptions _parallelOptions;
        private readonly JsonSerializerSettings _jsonSettings;

        private readonly Boolean _parallelHandlers;
        private readonly Int32 _maxRetries;
        private readonly Boolean _dropEventFatal;

        private readonly CancellationTokenSource _cancelToken;
        private readonly TaskScheduler _scheduler;

        private Int32 _processingQueueSize;
        private readonly Int32 _maxQueueSize;

        private Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private Metrics.Timer _eventsTimer = Metric.Timer("Event Duration", Unit.Events);
        private Metrics.Timer _handlerTimer = Metric.Timer("Event Handler Duration", Unit.Events);
        private Counter _queueSize = Metric.Counter("Event Queue Size", Unit.Events);

        private Counter _delayedSize = Metric.Counter("Events Delayed Queue", Unit.Events);
        private Meter _errorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        private static Action<NServiceBusDispatcher, Job> QueueTask = (dispatcher, x) =>
        {
            Task.Factory.StartNew(() =>
            {
                dispatcher.Process(x.Event, x.Descriptor, x.Position);
            }, creationOptions: TaskCreationOptions.None, cancellationToken: dispatcher._cancelToken.Token, scheduler: dispatcher._scheduler);
        };
        private static Action<NServiceBusDispatcher, DelayedJob> QueueDelayedTask = (dispatcher, x) =>
        {
            Task.Factory.StartNew(() =>
            {
                // Wait at least 250ms to retry, if its been longer just run it with no wait
                var diff = (DateTime.UtcNow.Ticks - x.FailedAt) / TimeSpan.TicksPerMillisecond;
                Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(250, diff)));
                dispatcher.Process(x.Event, x.Descriptor, x.Position, x.Retry + 1);
            }, creationOptions: TaskCreationOptions.None, cancellationToken: dispatcher._cancelToken.Token, scheduler: dispatcher._scheduler);
        };

        public NServiceBusDispatcher(IBuilder builder, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            _jsonSettings = jsonSettings;
            _parallelHandlers = settings.Get<Boolean>("ParallelHandlers");
            _maxRetries = settings.Get<Int32>("MaxRetries");
            _dropEventFatal = settings.Get<Boolean>("EventDropIsFatal");
            _maxQueueSize = settings.Get<Int32>("MaxQueueSize");

            _invokeCache = new ConcurrentDictionary<String, IList<Type>>();

            var parallelism = settings.Get<Int32>("SetEventStoreMaxDegreeOfParallelism");
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
            };

            _scheduler = new LimitedConcurrencyLevelTaskScheduler(1);
            _cancelToken = new CancellationTokenSource();
        }



        public void Dispatch(Object @event, IEventDescriptor descriptor = null, long? position = null)
        {
            if (_processingQueueSize >= _maxQueueSize)
                throw new SubscriptionCanceled("Processing queue overflow, too many items waiting to be processed");

            Interlocked.Increment(ref _processingQueueSize);
            _queueSize.Increment();
            QueueTask(this, new Job
            {
                Event = @event,
                Descriptor = descriptor,
                Position = position,
            });
        }

        // Todo: all the logging and timing can be moved into a "Debug Dispatcher" which can be registered as the IDispatcher if the user wants
        public void Process(Object @event, IEventDescriptor descriptor = null, long? position = null, int? retried = null)
        {
            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ

            var eventType = _mapper.GetMappedTypeFor(@event.GetType());

            if (!retried.HasValue)
            {
                _queueSize.Decrement();
                Interlocked.Decrement(ref _processingQueueSize);
            }
            else
                _delayedSize.Decrement();

            _eventsMeter.Mark();
            using (_eventsTimer.NewContext())
            {
                Logger.DebugFormat("Processing event {0}", eventType.FullName);

                var handlersToInvoke = _invokeCache.GetOrAdd(eventType.FullName,
                    (key) => _handlerRegistry.GetHandlerTypes(eventType).ToList());

                using (var childBuilder = _builder.CreateChildBuilder())
                {
                    var uows = new Stack<IEventUnitOfWork>();

                    var mutators = childBuilder.BuildAll<IEventMutator>();

                    var s = Stopwatch.StartNew();
                    if (mutators != null && mutators.Any())
                        foreach (var mutate in mutators)
                        {
                            Logger.DebugFormat("Mutating incoming event {0} with mutator {1}", eventType.FullName, mutate.GetType().FullName);
                            @event = mutate.MutateIncoming(@event, descriptor, position);
                        }

                    foreach (var uow in childBuilder.BuildAll<IEventUnitOfWork>())
                    {
                        uows.Push(uow);
                        uow.Builder = childBuilder;
                        uow.Begin();
                    }

                    s.Stop();
                    Logger.DebugFormat("UOW.Begin for event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);

                    try
                    {
                        s.Restart();

                        Action<Type> processor = (handler) =>
                         {
                             var instance = childBuilder.Build(handler);

                             using (_handlerTimer.NewContext())
                             {
                                 var handlerRetries = 0;
                                 var handlerSuccess = false;
                                 do
                                 {
                                     try
                                     {
                                         Logger.DebugFormat("Executing event {0} on handler {1}", eventType.FullName, handler.FullName);
                                         var handerWatch = Stopwatch.StartNew();
                                         handlerRetries++;
                                         _handlerRegistry.InvokeHandle(instance, @event);
                                         handerWatch.Stop();
                                         handlerSuccess = true;
                                         Logger.DebugFormat("Executing event {0} on handler {1} took {2} ms", eventType.FullName, handler.FullName, handerWatch.ElapsedMilliseconds);
                                     }
                                     catch (RetryException e)
                                     {
                                         Logger.InfoFormat("Received retry signal while dispatching event {0} to {1}. Retry: {2}/3\nException: {3}", eventType.FullName, handler.FullName, handlerRetries, e);
                                     }

                                 } while (!handlerSuccess && handlerRetries <= 3);

                                 if (!handlerSuccess)
                                 {
                                     Logger.ErrorFormat("Failed executing event {0} on handler {1}", eventType.FullName, handler.FullName);
                                     throw new RetryException(String.Format("Failed executing event {0} on handler {1}", eventType.FullName, handler.FullName));
                                 }
                             }

                         };

                        // Run each handler in parallel (or not)
                        if (_parallelHandlers)
                            Parallel.ForEach(handlersToInvoke, _parallelOptions, processor);
                        else
                        {
                            foreach (var handler in handlersToInvoke)
                                processor(handler);
                        }
                        s.Stop();
                        Logger.DebugFormat("Processing event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);

                    }
                    catch (Exception e)
                    {


                        var trailingExceptions = new List<Exception>();
                        while (uows.Count > 0)
                        {
                            var uow = uows.Pop();
                            try
                            {
                                uow.End(e);
                            }
                            catch (Exception endException)
                            {
                                trailingExceptions.Add(endException);
                            }
                        }
                        if (trailingExceptions.Any())
                        {
                            trailingExceptions.Insert(0, e);
                            e = new System.AggregateException(trailingExceptions);
                        }

                        Logger.InfoFormat("Encountered an error while processing {0}. Retry {1}/{2}\nWill move to the delayed queue for future processing.\nPayload: {3}\nException details:\n{4}", eventType.FullName, retried ?? 0, _maxRetries, JsonConvert.SerializeObject(@event, _jsonSettings), e);

                        _errorsMeter.Mark();
                        if (retried >= _maxRetries && _maxRetries != -1)
                        {
                            var message = String.Format("Encountered an error while processing {0}.  Ran out of retries, dropping event.\nPayload: {3}\nException details:\n{4}", eventType.FullName, JsonConvert.SerializeObject(@event, _jsonSettings), e);
                            if (_dropEventFatal)
                            {
                                Logger.Fatal(message);
                                throw new SubscriptionCanceled(message);
                            }

                            Logger.Error(message);
                            return;
                        }

                        _delayedSize.Increment();
                        QueueDelayedTask(this, new DelayedJob { Event = @event, Descriptor = descriptor, Position = position, Retry = (retried ?? 0) + 1, FailedAt = DateTime.UtcNow.Ticks });
                        return;
                    }

                    // Failures when executing UOW.End `could` be transient (network or disk hicup)
                    // A failure of 1 uow in a chain of 5 is a problem as it could create a mangled DB (partial update via one uow then crash)
                    // So we'll just keep retrying the failing UOW forever until it succeeds.  
                    s.Restart();
                    var success = false;
                    var retry = 0;
                    while (!success)
                    {
                        try
                        {
                            while (uows.Count > 0)
                            {
                                var uow = uows.Peek();
                                uow.End();
                                uows.Pop();
                            }
                            success = true;
                        }
                        catch (Exception e)
                        {
                            Logger.ErrorFormat("UOW.End failure while processing event {0} - retry {1}\nException:\n{2}", eventType.FullName, retry, e);
                            retry++;
                            Thread.Sleep(50);
                        }
                    }
                    s.Stop();
                    Logger.DebugFormat("UOW.End for event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);
                }
            }
        }


        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);

        }

        public void Dispose()
        {
            _cancelToken.Cancel();
        }
    }
}