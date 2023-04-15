using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using NServiceBus;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.Callbacks.Testing;

namespace Aggregates {
    public class TestableSession: TestableCallbackAwareSession {

        //static readonly IMessageCreator messageCreator = new MessageMapper();
        TMessage CreateInstance<TMessage>(Action<TMessage> action) {
            return messageCreator.CreateInstance(action);
        }
        TMessage CreateInstance<TMessage>() {
            return messageCreator.CreateInstance<TMessage>();
        }
        public void AcceptCommand<TCommand>() where TCommand : class, Aggregates.Messages.ICommand {
            AcceptCommand<TCommand>((command) => command.GetType() == typeof(TCommand));
        }
        public void RejectCommand<TCommand>() where TCommand : class, Aggregates.Messages.ICommand {
            RejectCommand<TCommand>((command) => command.GetType() == typeof(TCommand));
        }
        public void AcceptCommand<TCommand>(Func<TCommand, bool> match) where TCommand : class, Aggregates.Messages.ICommand {
            var accept = CreateInstance<Messages.Accept>();
            When(match, accept);
        }
        public void RejectCommand<TCommand>(Func<TCommand, bool> match) where TCommand : class, Aggregates.Messages.ICommand {
            var reject = CreateInstance<Messages.Reject>();
            When(match, reject);
        }
    }
}
