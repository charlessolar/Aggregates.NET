using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using NServiceBus;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.Callbacks.Testing;
using Microsoft.Extensions.DependencyInjection;
using Aggregates.Messages;

namespace Aggregates {
    public class TestableSession: TestableCallbackAwareSession {

        public readonly IServiceProvider ServiceProvider;
        public TestableSession() {

            ServiceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .BuildServiceProvider();
        }

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
            RespondToMessage(match, accept);
        }
        public void RejectCommand<TCommand>(Func<TCommand, bool> match) where TCommand : class, Aggregates.Messages.ICommand {
            var reject = CreateInstance<Messages.Reject>();
            RespondToMessage(match, reject);
        }
        public void RespondToMessage<TMessage, TResponse>(Func<TMessage, bool> match, TResponse response)
            where TMessage : class, Aggregates.Messages.IMessage
            where TResponse : class {
            When(match, response);
        }
    }
}
