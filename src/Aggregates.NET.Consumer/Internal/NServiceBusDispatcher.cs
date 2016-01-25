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
        private readonly ITargetBlock<ParellelJob> _parallelQueue;
        private readonly ITargetBlock<Job> _processingQueue;
        private readonly IBus _bus;
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;

        private readonly IDictionary<Type, IDictionary<Type, Boolean>> _parallelCache;
        private readonly IDictionary<String, Object> _handlerCache;
        private readonly IDictionary<String, IList<Type>> _invokeCache;

        private Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private Timer _eventsTimer = Metric.Timer("EventDuration", Unit.Events);
        private Counter _eventsConcurrent = Metric.Counter("ConcurrentEvents", Unit.Events);
        private Counter _parellelQueueSize = Metric.Counter("ParellelQueueSize", Unit.Events);
        private Counter _processingQueueSize = Metric.Counter("ProcessingQueueSize", Unit.Events);

        private Meter _errorsMeter = Metric.Meter("Errors", Unit.Errors);

        public NServiceBusDispatcher(IBus bus, IBuilder builder, ReadOnlySettings settings)
        {
            _bus = bus;
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            
            _parallelCache = new Dictionary<Type, IDictionary<Type, bool>>();
            _handlerCache = new ConcurrentDictionary<String, Object>();
            _invokeCache = new ConcurrentDictionary<String, IList<Type>>();

            var parallelism = settings.Get<Int32?>("SetEventStoreMaxDegreeOfParallelism") ?? Environment.ProcessorCount;
            var capacity = settings.Get<Tuple<Int32, Int32>>("SetEventStoreCapacity") ?? new Tuple<int, int>(1024, 1024);

            _parallelQueue = new ActionBlock<ParellelJob>((job) => ExecuteJob(job, true), 
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    BoundedCapacity = capacity.Item2,
                    SingleProducerConstrained = true
                });
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

            IList<Type> handlersToInvoke = null;
            if (!_invokeCache.TryGetValue(job.Event.GetType().FullName, out handlersToInvoke))
                _invokeCache[job.Event.GetType().FullName] = handlersToInvoke = _handlerRegistry.GetHandlerTypes(job.Event.GetType()).ToList();

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
                {
                    _parellelQueueSize.Increment();
                    _parallelQueue.SendAsync(parellelJob).Wait();
                }
                else
                    ExecuteJob(parellelJob);
            }
            _processingQueueSize.Decrement();

        }

        private void ExecuteJob(ParellelJob job, Boolean queued = false)
        {
            _eventsConcurrent.Increment();
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
                        Object handler = null;
                        if (!_handlerCache.TryGetValue(job.HandlerType.FullName, out handler))
                            _handlerCache[job.HandlerType.FullName] = handler = _builder.Build(job.HandlerType);

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
            if (queued)
                _parellelQueueSize.Decrement();

            _eventsConcurrent.Decrement();
        }

        public void Dispatch(Object @event)
        {
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