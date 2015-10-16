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
        private readonly IMessageHandlerRegistry _handlerRegistry;

        public NServiceBusDispatcher(IBus bus, IBuilder builder, ExecutionDataflowBlockOptions options = null)
        {
            _bus = bus;
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
            options = options ?? new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4 };

            var retry = new RetryPolicy(ErrorDetectionStrategy.On<Exception>(), 3, TimeSpan.FromMilliseconds(250));

            _queue = new ActionBlock<Job>(x =>
            {
                var uow = _builder.Build<IConsumeUnitOfWork>();

                retry.ExecuteAction(() =>
                {
                    try
                    {
                        var start = DateTime.UtcNow;

                        uow.Start();
                        var handler = _builder.Build(x.HandlerType);
                        _handlerRegistry.InvokeHandle(handler, x.Event);
                        uow.End();

                        var duration = (DateTime.UtcNow - start).TotalMilliseconds;
                        Logger.DebugFormat("Dispatching event {0} to handler {1} took {2} milliseconds", x.Event.GetType(), x.HandlerType, duration);
                    }
                    catch (RetryException e)
                    {
                        Logger.InfoFormat("Received retry signal while dispatching event {0}.  Message: {1}", x.Event.GetType(), e.Message);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorFormat("Error processing event {0}.  Exception: {1}", x.GetType(), ex);
                        uow.End(ex);
                        throw;
                    }
                });

            }, options);
        }

        public void Dispatch(Object @event)
        {
            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ
            var handlersToInvoke = _handlerRegistry.GetHandlerTypes(@event.GetType()).ToList();
            foreach( var handler in handlersToInvoke)
            {
                _queue.Post(new Job
                {
                    HandlerType = handler,
                    Event = @event
                });
            }
            
        }

        public void Dispatch<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Dispatch(@event);
        }
    }
}