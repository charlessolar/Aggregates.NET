using System;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class MutateIncomingEvents : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateIncomingEvents));

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (!(context.Message.Instance is IEvent)) return next();

            var mutators = context.Builder.BuildAll<IEventMutator>();
            if (mutators == null) return next();

            var mutated = (IEvent)context.Message.Instance;
            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating incoming event {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateIncoming(mutated, context.MessageHeaders);
            }
            context.UpdateMessageInstance(mutated);

            return next();
        }
    }
}
