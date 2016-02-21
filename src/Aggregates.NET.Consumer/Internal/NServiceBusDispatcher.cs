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
using Microsoft.Practices.TransientFaultHandling;
using Aggregates.Attributes;
using NServiceBus.MessageInterfaces;
using System.Collections.Concurrent;
using Metrics;

namespace Aggregates.Internal
{
    public class NServiceBusDispatcher : IDispatcher
    {
        private class ParellelJob
        {
            public Type HandlerType { get; set; }
            public Object Event { get; set; }
        }
        private class Job
        {
            public Object Event { get; set; }
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly ITargetBlock<Job> _processingQueue;
        private readonly IBus _bus;
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;

        private readonly IDictionary<Type, IDictionary<Type, Boolean>> _parallelCache;
        private readonly IDictionary<Type, Boolean> _eventParallelCache;
        private readonly ConcurrentDictionary<String, IList<Type>> _invokeCache;
        private readonly ExecutionDataflowBlockOptions _parallelOptions;

        private Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private Timer _eventsTimer = Metric.Timer("Event Duration", Unit.Events);
        private Counter _processingQueueSize = Metric.Counter("Processing Queue Size", Unit.Events);

        private Meter _errorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        public NServiceBusDispatcher(IBus bus, IBuilder builder, ReadOnlySettings settings)
        {
            _bus = bus;
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            
            _parallelCache = new Dictionary<Type, IDictionary<Type, Boolean>>();
            _eventParallelCache = new Dictionary<Type, Boolean>();
            _invokeCache = new ConcurrentDictionary<String, IList<Type>>();

            var parallelism = settings.Get<Int32?>("SetEventStoreMaxDegreeOfParallelism") ?? Environment.ProcessorCount;
            var capacity = settings.Get<Tuple<Int32, Int32>>("SetEventStoreCapacity") ?? new Tuple<int, int>(1024, 1024);

            _parallelOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = parallelism,
                BoundedCapacity = capacity.Item2,
            };

            _processingQueue = new ActionBlock<Job>((job) => Process(job),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
                    BoundedCapacity = capacity.Item1,
                });
        }

        private void Process(Job job)
        {
            _eventsMeter.Mark();

            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ


            Logger.DebugFormat("Processing event {0}", job.Event.GetType().FullName);
            var parallelQueue = new ActionBlock<ParellelJob>((x) => ExecuteJob(x), _parallelOptions);

            var handlersToInvoke = _invokeCache.GetOrAdd(job.Event.GetType().FullName, 
                (key) => _handlerRegistry.GetHandlerTypes(job.Event.GetType()).ToList());

            foreach (var handler in handlersToInvoke)
            {
                var eventType = _mapper.GetMappedTypeFor(job.Event.GetType());
                var parellelJob = new ParellelJob
                {
                    HandlerType = handler,
                    Event = job.Event
                };

                IDictionary<Type, Boolean> cached;
                Boolean parallel;
                if (!_parallelCache.TryGetValue(handler, out cached))
                {
                    cached = new Dictionary<Type, bool>();
                    _parallelCache[handler] = cached;
                }
                if (!cached.TryGetValue(eventType, out parallel))
                {

                    var interfaceType = typeof(IHandleMessages<>).MakeGenericType(eventType);

                    if (!interfaceType.IsAssignableFrom(handler))
                        continue;
                    var methodInfo = handler.GetInterfaceMap(interfaceType).TargetMethods.FirstOrDefault();
                    if (methodInfo == null)
                        continue;

                    parallel = handler.GetCustomAttributes(typeof(ParallelAttribute), false).Any() || methodInfo.GetCustomAttributes(typeof(ParallelAttribute), false).Any();
                    _parallelCache[handler][eventType] = parallel;
                }

                // If parallel - put on the threaded execution queue
                // Post returns false if its full - so keep retrying until it gets in
                if (parallel)
                    parallelQueue.SendAsync(parellelJob).Wait();
                else
                    ExecuteJob(parellelJob);
            }

            parallelQueue.Complete();

            parallelQueue.Completion.ContinueWith((_) =>
            {
                _processingQueueSize.Decrement();
            });

            Boolean dontWait = false;
            if(!_eventParallelCache.TryGetValue(job.Event.GetType(), out dontWait))
                _eventParallelCache[job.Event.GetType()] = dontWait = job.Event.GetType().GetCustomAttributes(typeof(ParallelAttribute), false).Any();

            if (dontWait)
                return;

            parallelQueue.Completion.Wait();
        }

        private void ExecuteJob(ParellelJob job)
        {
            Logger.DebugFormat("Executing event {0} on handler {1}", job.Event.GetType().FullName, job.HandlerType.FullName);
            var retries = 0;
            bool success = false;
            do
            {
                using (_eventsTimer.NewContext())
                {
                    var uow = _builder.Build<IConsumeUnitOfWork>();
                    try
                    {
                        var start = DateTime.UtcNow;

                        uow.Start();
                        Object handler = _builder.Build(job.HandlerType);
                        _handlerRegistry.InvokeHandle(handler, job.Event);
                        uow.End();
                        
                        success = true;
                    }
                    catch (RetryException e)
                    {
                        Logger.InfoFormat("Received retry signal while dispatching event {0}.  Message: {1}", job.Event.GetType(), e.Message);
                        uow.End(e);
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorFormat("Error processing event {0}.  Exception: {1}", job.Event.GetType(), ex);
                        uow.End(ex);
                        retries++;
                        System.Threading.Thread.Sleep(50);
                    }
                }
            } while (!success && retries < 3);
            if (!success)
                _errorsMeter.Mark();
            Logger.DebugFormat("Finished executing event {0} on handler {1}", job.Event.GetType().FullName, job.HandlerType.FullName);
        }

        public void Dispatch(Object @event)
        {
            Logger.DebugFormat("Queueing event {0} for processing", @event.GetType().FullName);
            _processingQueueSize.Increment();
            _processingQueue.SendAsync(new Job { Event = @event }).Wait();
        }

        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);
        }
    }
}