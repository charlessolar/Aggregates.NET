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
    internal class MutateOutgoingCommands : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly Meter Commands = Metric.Meter("Outgoing Commands", Unit.Items, tags: "debug");
        private static readonly ILog Logger = LogManager.GetLogger("MutateOutgoingCommands");

        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            var command = context.Message.Instance as ICommand;
            if (command == null) return next();

            Commands.Mark(context.Message.MessageType.FullName);

            var mutators= context.Builder.BuildAll<ICommandMutator>();
            if (!mutators.Any()) return next();

            IMutating mutated = new Mutating(command, context.Headers ?? new Dictionary<string, string>());
            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing command {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateOutgoing(mutated);
            }
            
            foreach (var header in mutated.Headers)
                context.Headers[header.Key] = header.Value;
            context.UpdateMessage(mutated.Message);

            return next();
        }
    }
    internal class MutateOutgoingCommandsRegistration : RegisterStep
    {
        public MutateOutgoingCommandsRegistration() : base(
            stepId: "MutateOutgoingCommands",
            behavior: typeof(MutateOutgoingCommands),
            description: "runs command mutators on outgoing commands"
        )
        {
            InsertAfter("MutateOutgoingMessages");
        }
    }

}
