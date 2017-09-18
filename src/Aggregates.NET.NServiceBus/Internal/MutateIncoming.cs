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
    internal class MutateIncoming : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("MutateIncoming");

        private readonly IMetrics _metrics;

        public MutateIncoming(IMetrics metrics)
        {
            _metrics = metrics;
        }

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            _metrics.Mark("Incoming Messages", Unit.Message);

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

                Logger.Write(LogLevel.Debug, () => $"Mutating incoming message {context.Message.MessageType.FullName} with mutator {type.FullName}");
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
            stepId: "MutateIncoming",
            behavior: typeof(MutateIncoming),
            description: "runs mutators on incoming messages"
        )
        {
            InsertAfter("MutateIncomingMessages");
        }
    }

}
