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
using Aggregates.Contracts;
using System.Diagnostics;
using Newtonsoft.Json;

namespace Aggregates.Internal
{
    public class NServiceBusDispatcher : IDispatcher
    {
        private class ParellelJob
        {
            public Object Handler { get; set; }
            public Object Event { get; set; }
        }
        private class Job
        {
            public Object Event { get; set; }
            public IEventDescriptor Descriptor { get; set; }
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly IBus _bus;
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;

        private readonly IDictionary<Type, IDictionary<Type, Boolean>> _parallelCache;
        private readonly IDictionary<Type, Boolean> _eventParallelCache;
        private readonly ConcurrentDictionary<String, IList<Type>> _invokeCache;
        private readonly ActionBlock<Job> _queue;
        private readonly ExecutionDataflowBlockOptions _parallelOptions;
        private readonly JsonSerializerSettings _jsonSettings;

        private Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private Timer _eventsTimer = Metric.Timer("Event Duration", Unit.Events);

        private Meter _errorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        public NServiceBusDispatcher(IBus bus, IBuilder builder, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
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
            var capacity = settings.Get<Int32?>("SetEventStoreCapacity") ?? 10000;

            _parallelOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = parallelism,
                BoundedCapacity = capacity,
            };
            _queue = new ActionBlock<Job>((x) => Process(x.Event, x.Descriptor), _parallelOptions);

        }

        private async Task Process(Object @event, IEventDescriptor descriptor)
        {
            _eventsMeter.Mark();

            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ

            Exception lastException = null;
            var retries = 0;
            bool success = false;
            var start = DateTime.UtcNow;
            do
            {
                Logger.DebugFormat("Processing event {0}", @event.GetType().FullName);


                var handlersToInvoke = _invokeCache.GetOrAdd(@event.GetType().FullName,
                    (key) => _handlerRegistry.GetHandlerTypes(@event.GetType()).ToList());

                using (var childBuilder = _builder.CreateChildBuilder())
                {
                    var uows = childBuilder.BuildAll<IConsumerUnitOfWork>();
                    var mutators = childBuilder.BuildAll<IEventMutator>();

                    try
                    {
                        if (mutators != null && mutators.Any())
                            foreach (var mutate in mutators)
                            {
                                Logger.DebugFormat("Mutating incoming event {0} with mutator {1}", @event.GetType().FullName, mutate.GetType().FullName);
                                @event = mutate.MutateIncoming(@event, descriptor);
                            }

                        if (uows != null && uows.Any())
                            foreach (var uow in uows)
                            {
                                uow.Builder = childBuilder;
                                uow.Begin();
                            }

                        var parallelQueue = new ActionBlock<ParellelJob>(job => ExecuteJob(job), _parallelOptions);

                        using (_eventsTimer.NewContext())
                        {
                            foreach (var handler in handlersToInvoke)
                            {
                                var eventType = _mapper.GetMappedTypeFor(@event.GetType());
                                var parellelJob = new ParellelJob
                                {
                                    Handler = childBuilder.Build(handler),
                                    Event = @event
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
                                    await parallelQueue.SendAsync(parellelJob);
                                else
                                    ExecuteJob(parellelJob);
                            }

                            parallelQueue.Complete();
                            await parallelQueue.Completion;
                        }


                        if (uows != null && uows.Any())
                            foreach (var uow in uows)
                                uow.End();

                        success = true;
                    }
                    catch (Exception ex)
                    {

                        if (uows != null && uows.Any())
                            foreach (var uow in uows)
                                uow.End(ex);

                        retries++;
                        lastException = ex;
                        System.Threading.Thread.Sleep(50);
                    }
                }
            } while (!success && retries < 5);
            if (!success)
                Logger.ErrorFormat("Failed to process event {0}.  Payload: \n{1}\n Exception: {2}", @event.GetType().FullName, JsonConvert.SerializeObject(@event, _jsonSettings), lastException);
        }

        private void ExecuteJob(ParellelJob job)
        {
            var retries = 0;
            bool success = false;
            do
            {

                try
                {
                    _handlerRegistry.InvokeHandle(job.Handler, job.Event);

                    success = true;
                }
                catch (RetryException e)
                {
                    Logger.InfoFormat("Received retry signal while dispatching event {0}.  Message: {1}", job.Event.GetType(), e.Message);
                    retries++;
                }
            } while (!success && retries < 3);
            if (!success)
            {
                _errorsMeter.Mark();
                Logger.ErrorFormat("Failed executing event {0} on handler {1}", job.Event.GetType().FullName, job.Handler.GetType().FullName);
            }
            else
                Logger.DebugFormat("Finished executing event {0} on handler {1}", job.Event.GetType().FullName, job.Handler.GetType().FullName);
        }

        public void Dispatch(Object @event, IEventDescriptor descriptor = null)
        {
            Logger.DebugFormat("Queueing event {0} for processing", @event.GetType().FullName);
            _queue.Post(new Job { Event = @event, Descriptor = descriptor });
        }

        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);
        }
    }
}