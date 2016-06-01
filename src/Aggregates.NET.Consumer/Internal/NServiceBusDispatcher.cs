using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private readonly IBus _bus;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;
        private readonly IInvokeObjects _objectInvoker;

        private readonly ConcurrentDictionary<String, IList<Type>> _invokeCache;
        private readonly ParallelOptions _parallelOptions;

        private readonly Boolean _parallelHandlers;
        private readonly Int32 _maxRetries;
        private readonly Boolean _dropEventFatal;
        private readonly Int32 _slowAlert;

        private readonly TaskProcessor _processor;

        private Int32 _processingQueueSize;
        private readonly Int32 _maxQueueSize;

        private static Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private static Metrics.Timer _eventsTimer = Metric.Timer("Event Duration", Unit.Events);
        private static Metrics.Timer _handlerTimer = Metric.Timer("Event Handler Duration", Unit.Events);
        private static Counter _queueSize = Metric.Counter("Event Queue Size", Unit.Events);

        private static Meter _errorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        private void QueueTask(Job x)
        {
            Interlocked.Increment(ref _processingQueueSize);
            _queueSize.Increment();

            if (_processingQueueSize % 10 == 0 || Logger.IsDebugEnabled)
            {
                var eventType = _mapper.GetMappedTypeFor(x.Event.GetType());
                var msg = String.Format("Queueing event {0} at position {1}.  Size of queue: {2}/{3}", eventType.FullName, x.Position, _processingQueueSize, _maxQueueSize);
                if (_processingQueueSize % 10 == 0)
                    Logger.Info(msg);
                else
                    Logger.Debug(msg);
            }

            _processor.Queue(async () =>
            {
                await Process(x.Event, x.Descriptor, x.Position);
                _queueSize.Decrement();
                Interlocked.Decrement(ref _processingQueueSize);
            });

        }

        public NServiceBusDispatcher(IBuilder builder, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _builder = builder;
            _bus = builder.Build<IBus>();
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            _objectInvoker = builder.Build<IInvokeObjects>();
            _parallelHandlers = settings.Get<Boolean>("ParallelHandlers");
            _maxRetries = settings.Get<Int32>("MaxRetries");
            _dropEventFatal = settings.Get<Boolean>("EventDropIsFatal");
            _maxQueueSize = settings.Get<Int32>("MaxQueueSize");
            _slowAlert = settings.Get<Int32>("SlowAlertThreshold");

            _invokeCache = new ConcurrentDictionary<String, IList<Type>>();

            var parallelism = settings.Get<Int32>("Parallelism");
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
            };

            _processor = new TaskProcessor(parallelism);
        }



        public void Dispatch(Object @event, IEventDescriptor descriptor = null, long? position = null)
        {
            if (_processingQueueSize >= _maxQueueSize)
                throw new SubscriptionCanceled("Processing queue overflow, too many items waiting to be processed");

            QueueTask(new Job
            {
                Event = @event,
                Descriptor = descriptor,
                Position = position,
            });
        }

        // Todo: all the logging and timing can be moved into a "Debug Dispatcher" which can be registered as the IDispatcher if the user wants
        private async Task Process(Object @event, IEventDescriptor descriptor = null, long? position = null)
        {

            var eventType = _mapper.GetMappedTypeFor(@event.GetType());
            Stopwatch s = new Stopwatch();

            var handleContext = new HandleContext
            {
                Mapper = _mapper,
                Bus = _bus,
                EventDescriptor = descriptor
            };

            using (_eventsTimer.NewContext())
            {
                if (Logger.IsDebugEnabled)
                    Logger.DebugFormat("Processing event {0} at position {1}.  Size of queue: {2}/{3}", eventType.FullName, position, _processingQueueSize, _maxQueueSize);


                var success = false;
                var retry = 0;
                do
                {

                    using (var childBuilder = _builder.CreateChildBuilder())
                    {
                        var handlerGenericType = typeof(IHandleMessagesAsync<>).MakeGenericType(eventType);
                        List<dynamic> handlers = childBuilder.BuildAll(handlerGenericType).ToList();

                        if (handlers.Count == 0)
                            return;

                        var uows = new ConcurrentStack<IEventUnitOfWork>();

                        var mutators = childBuilder.BuildAll<IEventMutator>();

                        if (Logger.IsDebugEnabled)
                            s.Restart();
                        if (mutators != null && mutators.Any())
                            Parallel.ForEach(mutators, _parallelOptions, mutate =>
                            {
                                //if (Logger.IsDebugEnabled)
                                Logger.DebugFormat("Mutating incoming event {0} with mutator {1}", eventType.FullName, mutate.GetType().FullName);
                                @event = mutate.MutateIncoming(@event, descriptor, position);
                            });

                        await childBuilder.BuildAll<IEventUnitOfWork>().ForEachAsync(2, async (uow) =>
                        {
                            uows.Push(uow);
                            uow.Builder = childBuilder;
                            await uow.Begin();
                        });

                        if (Logger.IsDebugEnabled)
                        {
                            s.Stop();
                            Logger.DebugFormat("UOW.Begin for event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);
                        }
                        try
                        {
                            if (Logger.IsDebugEnabled)
                                s.Restart();

                            Func<dynamic, Task> processor = async (handler) =>
                             {
                                 using (_handlerTimer.NewContext())
                                 {
                                     var handlerRetries = 0;
                                     var handlerSuccess = false;
                                     do
                                     {
                                         try
                                         {
                                             Stopwatch handlerWatch = Stopwatch.StartNew();
                                             if (Logger.IsDebugEnabled)
                                             {
                                                 Logger.DebugFormat("Executing event {0} on handler {1}", eventType.FullName, handler.GetType().FullName);
                                             }
                                             var lambda = _objectInvoker.Invoker(handler, eventType);

                                             await lambda(handler, @event, handleContext);

                                             handlerWatch.Stop();
                                             if (Logger.IsDebugEnabled)
                                             {
                                                 Logger.DebugFormat("Executing event {0} on handler {1} took {2} ms", eventType.FullName, handler.GetType().FullName, handlerWatch.ElapsedMilliseconds);
                                             }
                                             if (handlerWatch.ElapsedMilliseconds > _slowAlert)
                                             {
                                                 Logger.WarnFormat(" - SLOW ALERT - Executing event {0} on handler {1} took {2} ms", eventType.FullName, handler.GetType().FullName, handlerWatch.ElapsedMilliseconds);
                                             }
                                             handlerSuccess = true;
                                         }
                                         catch (RetryException e)
                                         {
                                             Logger.InfoFormat("Received retry signal while dispatching event {0} to {1}. Retry: {2}/3\nException: {3}", eventType.FullName, handler.FullName, handlerRetries, e);
                                             handlerRetries++;
                                         }

                                     } while (!handlerSuccess && (_maxRetries == -1 || handlerRetries <= _maxRetries));

                                     if (!handlerSuccess)
                                     {
                                         Logger.ErrorFormat("Failed executing event {0} on handler {1}", eventType.FullName, handler.FullName);
                                         throw new RetryException(String.Format("Failed executing event {0} on handler {1}", eventType.FullName, handler.FullName));
                                     }
                                 }

                             };

                            // Run each handler in parallel (or not) (if handler ever is ASYNC can't use Parallel)
                            if (_parallelHandlers)
                                await handlers.ForEachAsync(_parallelOptions.MaxDegreeOfParallelism, processor);
                            else
                            {
                                foreach (var handler in handlers)
                                    await processor(handler);
                            }

                            if (Logger.IsDebugEnabled)
                            {
                                s.Stop();
                                Logger.DebugFormat("Processing event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);
                            }

                            s.Restart();
                            await uows.Generate().ForEachAsync(2, async (uow) =>
                            {
                                try
                                {
                                    await uow.End();
                                }
                                catch
                                {
                                    // If it failed it needs to go back on the stack
                                    uows.Push(uow);
                                    throw;
                                }
                            });
                            s.Stop();
                            if (Logger.IsDebugEnabled)
                            {
                                Logger.DebugFormat("UOW.End for event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);
                            }
                            if (s.ElapsedMilliseconds > _slowAlert)
                            {
                                Logger.WarnFormat(" - SLOW ALERT - UOW.End for event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);
                            }
                        }
                        catch (Exception e)
                        {
                            var trailingExceptions = new ConcurrentBag<Exception>();
                            await uows.Generate().ForEachAsync(2, async (uow) =>
                            {
                                try
                                {
                                    await uow.End(e);
                                }
                                catch (Exception endException)
                                {
                                    trailingExceptions.Add(endException);
                                }
                            });
                            if (trailingExceptions.Any())
                            {
                                var exceptions = trailingExceptions.ToList();
                                exceptions.Insert(0, e);
                                e = new System.AggregateException(exceptions);
                            }

                            // Only log if the event has failed more than half max retries indicating a non-transient error
                            if (retry > (_maxRetries / 2))
                                Logger.WarnFormat("Encountered an error while processing {0}. Retry {1}/{2}\nPayload: {3}\nException details:\n{4}", eventType.FullName, retry, _maxRetries, JsonConvert.SerializeObject(@event), e);
                            else
                                Logger.DebugFormat("Encountered an error while processing {0}. Retry {1}/{2}\nPayload: {3}\nException details:\n{4}", eventType.FullName, retry, _maxRetries, JsonConvert.SerializeObject(@event), e);

                            _errorsMeter.Mark();
                            retry++;
                            Thread.Sleep(150);
                            continue;
                        }
                    }
                    success = true;
                } while (!success && (_maxRetries == -1 || retry < _maxRetries));

                if (!success)
                {
                    var message = String.Format("Encountered an error while processing {0}.  Ran out of retries, dropping event.\nPayload: {1}", eventType.FullName, JsonConvert.SerializeObject(@event));
                    if (_dropEventFatal)
                    {
                        Logger.Fatal(message);
                        throw new SubscriptionCanceled(message);
                    }

                    Logger.Error(message);
                }


            }
            _eventsMeter.Mark();
        }


        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);

        }

        public void Dispose()
        {
            _processor.Dispose();
        }
    }
}