using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Logging;
using Metrics;
using Aggregates.Extensions;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    internal class MutateIncomingEvents : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateIncomingEvents));

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (context.Message.Instance is IEvent)
            {
                var mutators = context.Builder.BuildAll<IEventMutator>();
                var mutated = context.Message.Instance as IEvent;
                if (mutators != null && mutators.Any())
                    foreach (var mutator in mutators)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Mutating incoming event {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                        mutated = mutator.MutateIncoming(mutated, context.MessageHeaders);
                    }
                context.UpdateMessageInstance(mutated);
            }

            return next();
        }
    }
}
