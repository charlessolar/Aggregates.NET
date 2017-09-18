using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class MutateOutgoing : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("MutateOutgoing");

        private readonly IMetrics _metrics;

        public MutateOutgoing(IMetrics metrics)
        {
            _metrics = metrics;
        }

        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            _metrics.Mark("Outgoing Messages", Unit.Message);

            IMutating mutated = new Mutating(context.Message.Instance, context.Headers ?? new Dictionary<string, string>());

            var mutators = MutationManager.Registered.ToList();
            if (!mutators.Any()) return next();

            IContainer container;
            // If theres a current container in the pipeline, use that
            if (!context.Extensions.TryGet<IContainer>(out container))
                container = Configuration.Settings.Container;

            foreach (var type in mutators)
            {
                var mutator = (IMutate)container.Resolve(type);

                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing message {context.Message.MessageType.FullName} with mutator {type.FullName}");
                mutated = mutator.MutateOutgoing(mutated);
            }
            
            foreach (var header in mutated.Headers)
                context.Headers[header.Key] = header.Value;
            context.UpdateMessage(mutated.Message);

            return next();
        }
    }
    internal class MutateOutgoingRegistration : RegisterStep
    {
        public MutateOutgoingRegistration() : base(
            stepId: "MutateOutgoing",
            behavior: typeof(MutateOutgoing),
            description: "runs mutators on outgoing messages"
        )
        {
            InsertAfter("MutateOutgoingMessages");
        }
    }

}
