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
    internal class MutateIncomingCommands : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger("MutateIncomingCommands");
        private static readonly Meter Commands = Metric.Meter("Incoming Commands", Unit.Items, tags: "debug");

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var command = context.Message.Instance as ICommand;
            if (command == null) return next();

            Commands.Mark(context.Message.MessageType.FullName);
            
            var mutators = context.Builder.BuildAll<ICommandMutator>();
            if (!mutators.Any()) return next();

            IMutating mutated = new Mutating(command, context.Headers);
            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating incoming command {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateIncoming(mutated);
            }

            // Todo: maybe have a small bool to set if they actually changed headers, save some cycles
            foreach (var header in mutated.Headers)
                context.Headers[header.Key] = header.Value;
            context.UpdateMessageInstance(mutated.Message);

            return next();
        }
    }
    internal class MutateIncomingCommandRegistration : RegisterStep
    {
        public MutateIncomingCommandRegistration() : base(
            stepId: "MutateIncomingCommands",
            behavior: typeof(MutateIncomingCommands),
            description: "Running command mutators for incoming messages"
        )
        {
            InsertAfter("ApplicationUnitOfWork");
        }
    }
}
