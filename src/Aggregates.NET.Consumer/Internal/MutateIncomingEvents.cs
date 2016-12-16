using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class MutateIncomingEvents : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly Meter Events = Metric.Meter("Incoming Events", Unit.Items);
        private static readonly ILog Logger = LogManager.GetLogger("MutateIncomingEvents");

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var @event = context.Message.Instance as IEvent;
            if (@event == null) return next();

            Events.Mark();
            
            var mutators = context.Builder.BuildAll<IEventMutator>();
            if (!mutators.Any()) return next();

            IMutating mutated = new Mutating(@event, context.Headers);
            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating incoming event {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateIncoming(mutated);
            }

            foreach (var header in mutated.Headers)
                context.Headers[header.Key] = header.Value;
            context.UpdateMessageInstance(mutated.Message);

            return next();
        }
    }

    internal class MutateIncomingRegistration : RegisterStep
    {
        public MutateIncomingRegistration() : base(
            stepId: "MutateIncomingEvents",
            behavior: typeof(MutateIncomingEvents),
            description: "Running event mutators for incoming messages"
        )
        {
            InsertAfter("ApplicationUnitOfWork");
        }
    }
}
