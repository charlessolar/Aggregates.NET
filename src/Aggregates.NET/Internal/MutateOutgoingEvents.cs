using System;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class MutateOutgoingEvents : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger("MutateOutgoingEvents");

        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            if (!(context.Message.Instance is IEvent)) return next();
            var mutators = context.Builder.BuildAll<IEventMutator>();
            if (mutators == null) return next();

            var mutated = (IEvent)context.Message.Instance;

            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing event {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateOutgoing(mutated);
            }

            context.UpdateMessage(mutated);

            return next();
        }
    }

}
