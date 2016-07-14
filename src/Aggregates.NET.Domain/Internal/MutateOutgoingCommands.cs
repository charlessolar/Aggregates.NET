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
            if (context.OutgoingLogicalMessage.Instance is ICommand)
            {

                var mutators = context.Builder.BuildAll<ICommandMutator>();
                var mutated = context.OutgoingLogicalMessage.Instance as ICommand;
                if (mutators != null && mutators.Any())
                    foreach (var mutator in mutators)
                    {
                        Logger.DebugFormat("Mutating outgoing command {0} with mutator {1}", context.OutgoingLogicalMessage.MessageType.FullName, mutator.GetType().FullName);
                        mutated = mutator.MutateOutgoing(mutated);
                    }
                context.Set("NServiceBus.OutgoingLogicalMessageKey", mutated);
            }

            next();
        }
    }

    internal class MutateOutgoingCommandsRegistration : RegisterStep
    {
        public MutateOutgoingCommandsRegistration()
            : base("MutateOutgoingCommands", typeof(MutateOutgoingCommands), "Running command mutators for outgoing messages")
        {
            InsertBefore(WellKnownStep.CreatePhysicalMessage);
        }
    }
}
