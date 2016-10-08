using System;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class MutateOutgoingCommands : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateOutgoingCommands));

        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            var command = context.Message.Instance as ICommand;
            if (command == null) return next();

            var mutators = context.Builder.BuildAll<ICommandMutator>();
            if (mutators == null) return next();

            var mutated = command;
            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing command {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateOutgoing(mutated);
            }

            context.UpdateMessage(mutated);

            return next();
        }
    }

}
