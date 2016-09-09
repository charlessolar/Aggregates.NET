using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Logging;
using Metrics;
using Aggregates.Extensions;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    internal class MutateOutgoingCommands : IBehavior<OutgoingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateOutgoingCommands));

        private readonly IBus _bus;
        public MutateOutgoingCommands(IBus bus)
        {
            _bus = bus;
        }

        public void Invoke(OutgoingContext context, Action next)
        {
            Invoke(new OutgoingContextWrapper(context), next);
        }


        public void Invoke(IOutgoingContextAccessor context, Action next)
        {
            if (context.OutgoingLogicalMessageInstance is ICommand)
            {
                var mutators = context.Builder.BuildAll<ICommandMutator>();
                var mutated = context.OutgoingLogicalMessageInstance as ICommand;
                if (mutators != null && mutators.Any())
                    foreach (var mutator in mutators)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Mutating outgoing command {context.OutgoingLogicalMessageMessageType.FullName} with mutator {mutator.GetType().FullName}");
                        mutated = mutator.MutateOutgoing(mutated);
                    }
                context.UpdateMessageInstance(mutated);
            }

            next();
        }
    }

    internal class MutateOutgoingCommandsRegistration : RegisterStep
    {
        public MutateOutgoingCommandsRegistration()
            : base("MutateOutgoingCommands", typeof(MutateOutgoingCommands), "Running command mutators for outgoing messages")
        {
            InsertAfter(WellKnownStep.MutateOutgoingMessages);
            InsertBefore(WellKnownStep.CreatePhysicalMessage);
        }
    }
}
