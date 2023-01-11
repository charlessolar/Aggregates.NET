using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.Callbacks.Testing;
using NServiceBus.Extensibility;
using NServiceBus.MessageInterfaces;
using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using NServiceBus.Persistence;
using NServiceBus.Pipeline;
using NServiceBus.Testing;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public class TestableContext : IMessageHandlerContext, IInvokeHandlerContext
    {
        static readonly IMessageCreator messageCreator = new MessageMapper();

        public readonly IServiceProvider ServiceProvider;
        public readonly ITestableDomain UoW;
        public readonly ITestableApplication App;
        public readonly ITestableProcessor Processor;
        protected readonly TestableMessageHandlerContext _ctx;
        protected readonly TestableCallbackAwareSession _session;
        protected readonly IdRegistry _ids;

        public TestableContext()
        {
            _ids = new IdRegistry();
            _ctx = new TestableMessageHandlerContext();

            UoW = new TestableDomain(this, _ids);
            App = new TestableApplication(_ids);
            Processor = new TestableProcessor();

            ServiceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .AddTransient<Contracts.IVersionRegistrar, TestableVersionRegistrar>()
                .AddTransient<IMessageSerializer, TestableMessageSerializer>()
                .AddTransient<IMessageCreator, MessageMapper>()
                .AddTransient<IMessageMapper, MessageMapper>()
                .AddTransient<IEventFactory, TestableEventFactory>()
                .AddTransient<IStoreEvents, TestableEventStore>()
                .AddTransient<IStoreSnapshots, TestableSnapshotStore>()
                .AddTransient<TestableVersionRegistrar>()
                .AddTransient<TestableEventFactory>()
                .AddTransient<TestableEventStore>()
                .AddTransient<TestableSnapshotStore>()
                .BuildServiceProvider();

            _ctx.Extensions.Set("CommandDestination", "");
            _ctx.Extensions.Set<UnitOfWork.IDomainUnitOfWork>(UoW);
            _ctx.Extensions.Set<UnitOfWork.IUnitOfWork>(App);
            _ctx.Extensions.Set<IProcessor>(Processor);
            _ctx.Extensions.Set<IServiceProvider>(ServiceProvider);

        }
        static TMessage CreateInstance<TMessage>(Action<TMessage> action)
        {
            return messageCreator.CreateInstance(action);
        }
        static TMessage CreateInstance<TMessage>()
        {
            return messageCreator.CreateInstance<TMessage>();
        }

        public TEvent Create<TEvent>(Action<TEvent> action) where TEvent : Messages.IEvent
        {
            return CreateInstance<TEvent>(action);
        }


        public TestableId Id()
        {
            return _ids.AnyId();
        }
        public TestableId Id(string named)
        {
            return _ids.MakeId(named);
        }
        public TestableId Id(int number)
        {
            return _ids.MakeId(number);
        }

        public ISynchronizedStorageSession SynchronizedStorageSession => _ctx.SynchronizedStorageSession;

        public string MessageId => _ctx.MessageId;

        public string ReplyToAddress => _ctx.ReplyToAddress;

        public IReadOnlyDictionary<string, string> MessageHeaders => _ctx.MessageHeaders as IReadOnlyDictionary<string, string>;

        public ContextBag Extensions => _ctx.Extensions;
        public CancellationToken CancellationToken => _ctx.CancellationToken;

        public void DoNotContinueDispatchingCurrentMessageToHandlers()
        {
            _ctx.DoNotContinueDispatchingCurrentMessageToHandlers();
        }

        public Task ForwardCurrentMessageTo(string destination)
        {
            return _ctx.ForwardCurrentMessageTo(destination);
        }

        public Task HandleCurrentMessageLater()
        {
            throw new NotImplementedException();
        }

        public Task Publish(object message, PublishOptions options)
        {
            return _ctx.Publish(message, options);
        }

        public Task Publish<T>(Action<T> messageConstructor, PublishOptions publishOptions)
        {
            return _ctx.Publish<T>(messageConstructor, publishOptions);
        }

        public Task Reply(object message, ReplyOptions options)
        {
            return _ctx.Reply(message, options);
        }

        public Task Reply<T>(Action<T> messageConstructor, ReplyOptions options)
        {
            return _ctx.Reply(messageConstructor, options);
        }

        public Task Send(object message, SendOptions options)
        {
            return _ctx.Send(message, options);
        }

        public Task Send<T>(Action<T> messageConstructor, SendOptions options)
        {
            return _ctx.Send(messageConstructor, options);
        }

        public void AcceptCommand<TCommand>() where TCommand : class, Aggregates.Messages.ICommand
        {
            AcceptCommand<TCommand>((command) => command.GetType() == typeof(TCommand));
        }
        public void RejectCommand<TCommand>() where TCommand : class, Aggregates.Messages.ICommand
        {
            RejectCommand<TCommand>((command) => command.GetType() == typeof(TCommand));
        }
        public void AcceptCommand<TCommand>(Func<TCommand, bool> match) where TCommand : class, Aggregates.Messages.ICommand
        {
            var accept = CreateInstance<Messages.Accept>();
            _session.When(match, accept);
        }
        public void RejectCommand<TCommand>(Func<TCommand, bool> match) where TCommand : class, Aggregates.Messages.ICommand
        {
            var reject = CreateInstance<Messages.Reject>();
            _session.When(match, reject);
        }

        public RepliedMessage<object>[] RepliedMessages => _ctx.RepliedMessages;
        public string[] ForwardedMessages => _ctx.ForwardedMessages;
        public SentMessage<object>[] SentMessages
        {
            get
            {
                var serializer = ServiceProvider.GetRequiredService<IMessageSerializer>();
                var versionRegistrar = ServiceProvider.GetRequiredService<IVersionRegistrar>();

                // Combine commands sent via "Saga" into sent messages
                var sagas = _ctx.SentMessages.Where(x => x.Message is Sagas.StartCommandSaga);
                var translatedCommands = sagas.SelectMany(x => (x.Message as Sagas.StartCommandSaga).Commands.Select(y =>
                {
                    // The data is serialized into strings to preserve types 
                    // deserialize for the testing
                    var message = serializer.Deserialize(
                       versionRegistrar.GetNamedType(y.Version),
                       y.Message.AsByteArray()
                       );

                    return new SentMessage<object>(message, x.Options);
                }));

                return _ctx.SentMessages.Where(x => !(x.Message is Sagas.StartCommandSaga)).Concat(translatedCommands).ToArray();
            }
        }

        public MessageHandler MessageHandler => throw new NotImplementedException();

        public Dictionary<string, string> Headers => throw new NotImplementedException();

        public object MessageBeingHandled => new TestMessage();

        public bool HandlerInvocationAborted => throw new NotImplementedException();

        public MessageMetadata MessageMetadata => throw new NotImplementedException();

        public IServiceProvider Builder => ServiceProvider;
    }
}
