using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageMutator;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class CommandMutator : IMessageMutator
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandMutator));
        private readonly IBuilder _builder;

        public CommandMutator(IBuilder builder)
        {
            _builder = builder;
        }

        public object MutateOutgoing(object message)
        {
            if(message is ICommand)
            {
                var mutators = _builder.BuildAll<ICommandMutator>();
                if (mutators != null && mutators.Any())
                    foreach (var mutator in mutators)
                    {
                        //if (Logger.IsDebugEnabled)
                        Logger.DebugFormat("Mutating outgoing command {0} with mutator {1}", message.GetType().FullName, mutator.GetType().FullName);
                        message = mutator.MutateOutgoing(message as ICommand);
                    }
            }
            return message;
        }

        public object MutateIncoming(object message)
        {
            if(message is ICommand)
            {
                var mutators = _builder.BuildAll<ICommandMutator>();
                foreach (var mutator in mutators)
                {
                    //if (Logger.IsDebugEnabled)
                    Logger.DebugFormat("Mutating incoming command {0} with mutator {1}", message.GetType().FullName, mutator.GetType().FullName);
                    message = mutator.MutateIncoming(message as ICommand);
                }
            }
            return message;
        }
    }
}
