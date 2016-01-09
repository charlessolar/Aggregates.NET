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
using System.Threading;

namespace Aggregates.Internal
{
    public class NServiceBusDispatcher : IDispatcher
    {
        private class Job
        {
            public Type HandlerType { get; set; }
            public Object Event { get; set; }
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly ITargetBlock<Job> _queue;
        private readonly IBus _bus;
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;
        private readonly Dictionary<Type, Dictionary<Type, Boolean>> _parallelCache;

        public NServiceBusDispatcher(IBus bus, IBuilder builder)
        {
            _bus = bus;
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                BoundedCapacity = 200,
            };
            _parallelCache = new Dictionary<Type, Dictionary<Type, bool>>();

            _queue = new ActionBlock<Job>((job) => ExecuteJob(job), options);
        }

        private void ExecuteJob(Job job)
        {
            var retries = 0;
            bool success = false;
            while (!success && retries < 3)
            {
                var uow = _builder.Build<IConsumeUnitOfWork>();
                try
                {
                    var start = DateTime.UtcNow;

                    uow.Start();
                    var handler = _builder.Build(job.HandlerType);
                    _handlerRegistry.InvokeHandle(handler, job.Event);
                    uow.End();

                    var duration = (DateTime.UtcNow - start).TotalMilliseconds;
                    Logger.DebugFormat("Dispatching event {0} to handler {1} took {2} milliseconds", job.Event.GetType(), job.HandlerType, duration);
                    success = true;
                }
                catch (RetryException e)
                {
                    Logger.InfoFormat("Received retry signal while dispatching event {0}.  Message: {1}", job.Event.GetType(), e.Message);
                    uow.End(e);
                    Thread.Sleep(250);
                }
                catch (Exception ex)
                {
                    Logger.ErrorFormat("Error processing event {0}.  Exception: {1}", job.Event.GetType(), ex);
                    uow.End(ex);
                    retries++;
                    Thread.Sleep(250);
                }
            };
        }

        public void Dispatch(Object @event)
        {
            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ
            var handlersToInvoke = _handlerRegistry.GetHandlerTypes(@event.GetType()).ToList();
            foreach (var handler in handlersToInvoke)
            {
                var eventType = _mapper.GetMappedTypeFor(@event.GetType());
                var job = new Job
                {
                    HandlerType = handler,
                    Event = @event
                };

                Dictionary<Type, Boolean> cached;
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
                    while (!_queue.Post(job)) ;
                else
                    ExecuteJob(job);
            }

        }

        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);
        }
    }
}