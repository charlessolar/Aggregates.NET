using Microsoft.Practices.TransientFaultHandling;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Aggregates.Internal
{
    public class DataFlowProcessor : IProcessor
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DataFlowProcessor));
        private readonly ITargetBlock<Object> _queue;
        private readonly IBuilder _builder;
        private readonly IMessageCreator _eventFactory;

        public DataFlowProcessor(IBuilder builder, IDispatcher dispatcher, ExecutionDataflowBlockOptions options = null)
        {
            options = options ?? new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4 };
            _builder = builder;
            _eventFactory = builder.Build<IMessageCreator>();

            var retry = new RetryPolicy(ErrorDetectionStrategy.On<Exception>(), 3, TimeSpan.FromMilliseconds(250));

            _queue = new ActionBlock<Object>(x =>
            {
                var uow = _builder.Build<IConsumeUnitOfWork>();

                retry.ExecuteAction(() =>
                {
                    try
                    {
                        uow.Start();
                        dispatcher.Dispatch(x);
                        uow.End();
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
        
        public void Push(Object @event)
        {
            _queue.Post(@event);
        }

        public void Push<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            this.Push(@event);
        }
    }
}
