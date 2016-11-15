using System;
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
        private static readonly Meter Commands = Metric.Meter("Incoming Commands", Unit.Items);
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateIncomingCommands));

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (!(context.Message.Instance is ICommand)) return next();

            Commands.Mark();
            var mutators = context.Builder.BuildAll<ICommandMutator>();
            var mutated = (ICommand)context.Message.Instance;
            if (mutators == null) return next();

            foreach (var mutator in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating incoming command {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                mutated = mutator.MutateIncoming(mutated, context.MessageHeaders);
            }
            context.UpdateMessageInstance(mutated);

            return next();
        }
    }
    internal class MutateIncomingRegistration : RegisterStep
    {
        public MutateIncomingRegistration() : base(
            stepId: "MutateIncomingCommands",
            behavior: typeof(MutateIncomingCommands),
            description: "Running command mutators for incoming messages"
        )
        {
            InsertAfter("ApplicationUnitOfWork");
        }
    }
}
