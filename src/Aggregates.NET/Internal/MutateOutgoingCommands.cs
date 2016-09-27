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
    internal class MutateOutgoingCommands : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MutateOutgoingCommands));
        
        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            if (context.Message.Instance is ICommand)
            {
                var mutators = context.Builder.BuildAll<ICommandMutator>();
                var mutated = context.Message.Instance as ICommand;
                if (mutators != null && mutators.Any())
                    foreach (var mutator in mutators)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Mutating outgoing command {context.Message.MessageType.FullName} with mutator {mutator.GetType().FullName}");
                        mutated = mutator.MutateOutgoing(mutated);
                    }
                
                context.UpdateMessage(mutated);
            }

            return next();
        }
    }
    
}
