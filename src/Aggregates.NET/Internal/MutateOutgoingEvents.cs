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
    internal class MutateOutgoingEvents : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly Meter Events = Metric.Meter("Outgoing Events", Unit.Items, tags: "debug");
        private static readonly ILog Logger = LogManager.GetLogger("MutateOutgoingEvents");

        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            var @event = context.Message.Instance as IEvent;
            if (@event == null) return next();

            Events.Mark(context.Message.MessageType.FullName);
            
            var mutators=context.Builder.BuildAll<IEventMutator>();
            if (!mutators.Any()) return next();

            IMutating mutated = new Mutating(@event, context.Headers ?? new Dictionary<string, string>());
            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing event {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateOutgoing(mutated);
            }

            foreach (var header in mutated.Headers)
                context.Headers[header.Key] = header.Value;
            context.UpdateMessage(mutated.Message);

            return next();
        }
    }
    internal class MutateOutgoingEventsRegistration : RegisterStep
    {
        public MutateOutgoingEventsRegistration() : base(
            stepId: "MutateOutgoingEvents",
            behavior: typeof(MutateOutgoingEvents),
            description: "runs command mutators on outgoing events"
        )
        {
            InsertAfter("MutateOutgoingMessages");
        }
    }

}
