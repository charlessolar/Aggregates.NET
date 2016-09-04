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
    internal class MutateIncomingCommands : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateIncomingCommands));
        
        private readonly IBus _bus;
        public MutateIncomingCommands(IBus bus)
        {
            _bus = bus;
        }


        public void Invoke(IncomingContext context, Action next)
        {
            if (context.IncomingLogicalMessage.Instance is ICommand)
            {

                var mutators = context.Builder.BuildAll<ICommandMutator>();
                var mutated = context.IncomingLogicalMessage.Instance as ICommand;
                if (mutators != null && mutators.Any())
                    foreach (var mutator in mutators)
                    {
                        Logger.WriteFormat(LogLevel.Debug, "Mutating incoming command {0} with mutator {1}", context.IncomingLogicalMessage.MessageType.FullName, mutator.GetType().FullName);
                        mutated = mutator.MutateIncoming(mutated);
                    }
                context.IncomingLogicalMessage.UpdateMessageInstance(mutated);
            }

            next();
        }
    }

    internal class MutateIncomingCommandsRegistration : RegisterStep
    {
        public MutateIncomingCommandsRegistration()
            : base("MutateIncomingCommands", typeof(MutateIncomingCommands), "Running command mutators for incoming messages")
        {
            InsertBefore(WellKnownStep.LoadHandlers);
            InsertAfter(WellKnownStep.MutateIncomingMessages);
        }
    }
}
