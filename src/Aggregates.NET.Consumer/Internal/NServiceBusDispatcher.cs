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
            public Object Event { get; set; }
            public IEventDescriptor Descriptor { get; set; }
        }

        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;
        private readonly IMessageMapper _mapper;
        private readonly IMessageHandlerRegistry _handlerRegistry;
        
        private readonly ConcurrentDictionary<String, IList<Type>> _invokeCache;
        private readonly ActionBlock<Job> _queue;
        private readonly JsonSerializerSettings _jsonSettings;

        private static DateTime Stamp = DateTime.UtcNow;

        private Meter _eventsMeter = Metric.Meter("Events", Unit.Events);
        private Metrics.Timer _eventsTimer = Metric.Timer("Event Duration", Unit.Events);

        private Meter _errorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        public NServiceBusDispatcher(IBuilder builder, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _mapper = builder.Build<IMessageMapper>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            _jsonSettings = jsonSettings;
            
            _invokeCache = new ConcurrentDictionary<String, IList<Type>>();

            var parallelism = settings.Get<Int32?>("SetEventStoreMaxDegreeOfParallelism") ?? Environment.ProcessorCount;
            var capacity = settings.Get<Int32?>("SetEventStoreCapacity") ?? 10000;
            
            _queue = new ActionBlock<Job>((x) => Process(x.Event, x.Descriptor), 
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    BoundedCapacity = capacity,
                });

        }

        private void Process(Object @event, IEventDescriptor descriptor)
        {
            Thread.CurrentThread.Rename("Dispatcher");
            _eventsMeter.Mark();

            var eventType = _mapper.GetMappedTypeFor(@event.GetType());

            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ

            Exception lastException = null;
            var retries = 0;
            bool success = false;
            do
            {
                Logger.DebugFormat("Processing event {0}", eventType.FullName);


                retries++;
                var handlersToInvoke = _invokeCache.GetOrAdd(eventType.FullName,
                    (key) => _handlerRegistry.GetHandlerTypes(eventType).ToList());

                using (var childBuilder = _builder.CreateChildBuilder())
                {
                    var uows = childBuilder.BuildAll<IConsumerUnitOfWork>();
                    var mutators = childBuilder.BuildAll<IEventMutator>();

                    try
                    {
                        using (_eventsTimer.NewContext())
                        {
                            if (mutators != null && mutators.Any())
                                foreach (var mutate in mutators)
                                {
                                    Logger.DebugFormat("Mutating incoming event {0} with mutator {1}", eventType.FullName, mutate.GetType().FullName);
                                    @event = mutate.MutateIncoming(@event, descriptor);
                                }

                            if (uows != null && uows.Any())
                                foreach (var uow in uows)
                                {
                                    uow.Builder = childBuilder;
                                    uow.Begin();
                                }
                            
                            foreach (var handler in handlersToInvoke)
                            {

                                var handlerRetries = 0;
                                var handlerSuccess = false;
                                do
                                {
                                    try
                                    {
                                        var instance = childBuilder.Build(handler);
                                        handlerRetries++;
                                        _handlerRegistry.InvokeHandle(instance, @event);
                                        handlerSuccess = true;
                                    }
                                    catch (RetryException e)
                                    {
                                        Logger.InfoFormat("Received retry signal while dispatching event {0} to {1}. Retry: {2}\nException: {3}", eventType.FullName, handler.FullName, handlerRetries, e);
                                    }

                                } while (!handlerSuccess && handlerRetries <= 3);

                                if (!handlerSuccess)
                                {
                                    Logger.ErrorFormat("Failed executing event {0} on handler {1}", eventType.FullName, handler.FullName);
                                    throw new RetryException(String.Format("Failed executing event {0} on handler {1}", eventType.FullName, handler.FullName));
                                }
                            }
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

                        lastException = ex;
                        Thread.Sleep(50);
                    }
                }
            } while (!success && retries <= 3);
            if (!success)
            {
                _errorsMeter.Mark();
                Logger.ErrorFormat("Failed to process event {0}.  Payload: \n{1}\n Exception: {2}", @event.GetType().FullName, JsonConvert.SerializeObject(@event, _jsonSettings), lastException);
            }
        }
        

        public void Dispatch(Object @event, IEventDescriptor descriptor = null)
        {
            _queue.SendAsync(new Job { Event = @event, Descriptor = descriptor }).Wait();
        }

        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);
        }
    }
}