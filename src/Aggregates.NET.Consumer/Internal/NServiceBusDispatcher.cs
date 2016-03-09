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
using System.Threading.Tasks.Dataflow;
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
    public class NServiceBusDispatcher : IDispatcher
    {
        private class Job
        {
            public Type EventType { get; set; }
            public Object Handler { get; set; }
            public Object Event { get; set; }
        }
        private class DelayedJob
        {
            public Object Event { get; set; }
            public IEventDescriptor Descriptor { get; set; }
            public long? Position { get; set; }
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;

        private readonly ConcurrentDictionary<String, IList<Type>> _invokeCache;
        private readonly ExecutionDataflowBlockOptions _parallelOptions;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly ActionBlock<DelayedJob> _delayedQueue;

        private static DateTime Stamp = DateTime.UtcNow;

        private Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private Metrics.Timer _eventsTimer = Metric.Timer("Event Duration", Unit.Events);
        private Metrics.Timer _handlerTimer = Metric.Timer("Event Handler Duration", Unit.Events);

        private Counter _delayedSize = Metric.Counter("Events Delayed Queue", Unit.Events);
        private Meter _errorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        public NServiceBusDispatcher(IBuilder builder, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            _jsonSettings = jsonSettings;

            _invokeCache = new ConcurrentDictionary<String, IList<Type>>();

            var parallelism = settings.Get<Int32>("SetEventStoreMaxDegreeOfParallelism");
            _parallelOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = parallelism,
            };
            _delayedQueue = new ActionBlock<DelayedJob>(x =>
            {
                Thread.Sleep(250);
                Dispatch(x.Event, x.Descriptor, x.Position, true);
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelism, BoundedCapacity = 128 });
        


        }
        public void Dispatch(Object @event, IEventDescriptor descriptor = null, long? position = null, Boolean delayed = false)
        {

            var queue = new ActionBlock<Job>((x) =>
            {

                var handlerType = x.Handler.GetType();
                Thread.CurrentThread.Rename("Dispatcher");
                using (_handlerTimer.NewContext())
                {
                    var handlerRetries = 0;
                    var handlerSuccess = false;
                    do
                    {
                        try
                        {
                            Logger.DebugFormat("Executing event {0} on handler {1}", x.EventType.FullName, handlerType.FullName);
                            var s = Stopwatch.StartNew();
                            handlerRetries++;
                            _handlerRegistry.InvokeHandle(x.Handler, x.Event);
                            s.Stop();
                            handlerSuccess = true;
                            Logger.DebugFormat("Executing event {0} on handler {1} took {2} ms", x.EventType.FullName, handlerType.FullName, s.ElapsedMilliseconds);
                        }
                        catch (RetryException e)
                        {
                            Logger.InfoFormat("Received retry signal while dispatching event {0} to {1}. Retry: {2}\nException: {3}", x.EventType.FullName, handlerType.FullName, handlerRetries, e);
                        }

                    } while (!handlerSuccess && handlerRetries <= 3);

                    if (!handlerSuccess)
                    {
                        Logger.ErrorFormat("Failed executing event {0} on handler {1}", x.EventType.FullName, handlerType.FullName);
                        throw new RetryException(String.Format("Failed executing event {0} on handler {1}", x.EventType.FullName, handlerType.FullName));
                    }
                }
            });


            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ

            var eventType = _mapper.GetMappedTypeFor(@event.GetType());

            _eventsMeter.Mark();
            using (_eventsTimer.NewContext())
            {
                Logger.DebugFormat("Processing event {0}", eventType.FullName);
                
                var handlersToInvoke = _invokeCache.GetOrAdd(eventType.FullName,
                    (key) => _handlerRegistry.GetHandlerTypes(eventType).ToList());
                
                using (var childBuilder = _builder.CreateChildBuilder())
                {
                    var uows = childBuilder.BuildAll<IEventUnitOfWork>();
                    var mutators = childBuilder.BuildAll<IEventMutator>();

                    var s = Stopwatch.StartNew();
                    if (mutators != null && mutators.Any())
                        foreach (var mutate in mutators)
                        {
                            Logger.DebugFormat("Mutating incoming event {0} with mutator {1}", eventType.FullName, mutate.GetType().FullName);
                            @event = mutate.MutateIncoming(@event, descriptor, position);
                        }

                    if (uows != null && uows.Any())
                        foreach (var uow in uows)
                        {
                            uow.Builder = childBuilder;
                            uow.Begin();
                        }

                    // Run each handler in parallel
                    foreach (var handler in handlersToInvoke)
                    {
                        var instance = childBuilder.Build(handler);

                        queue.Post(new Job { EventType = eventType, Handler = instance, Event = @event });

                    }

                    queue.Complete();

                    try
                    {
                        queue.Completion.Wait();

                        if (uows != null && uows.Any())
                            foreach (var uow in uows)
                                uow.End();
                        s.Stop();
                        Logger.DebugFormat("Processing event {0} took {1} ms", eventType.FullName, s.ElapsedMilliseconds);

                    }
                    catch (Exception e)
                    {

                        Logger.InfoFormat("Encountered an error while processing {0}.  Will move to the delayed queue for future processing.  Exception details:\n{1}", eventType.FullName, e);

                        if (uows != null && uows.Any())
                            foreach (var uow in uows)
                                uow.End(e);

                        _delayedSize.Increment();
                        _errorsMeter.Mark();
                        var delay = _delayedQueue.SendAsync(new DelayedJob { Event = @event, Descriptor = descriptor, Position = position });
                        delay.Wait();
                        if (!delay.Result)
                        {
                            Logger.Fatal("Too many events in error queue.  Shutting down");
                            throw new SubscriptionCanceled("Too many events in error queue");
                        }

                    }

                }
            }
            if (delayed)
                _delayedSize.Decrement();
        }
        

        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            ThreadPool.QueueUserWorkItem((_) =>
            {
                this.Dispatch(@event);
            });
        }
    }
}