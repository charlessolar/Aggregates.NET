using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Persistence;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class TestableContext : IMessageHandlerContext
    {

        public readonly TestableUnitOfWork UoW;
        private readonly TestableMessageHandlerContext _ctx;

        public TestableContext(TestableUnitOfWork uow)
        {
            UoW = uow;
            _ctx = new TestableMessageHandlerContext();
            _ctx.Extensions.Set<IDomainUnitOfWork>(UoW);
        }

        public TestableId Id()
        {
            return UoW.AnyId();
        }
        public TestableId Id(string named)
        {
            return UoW.MakeId(named);
        }
        public TestableId Id(int number)
        {
            return UoW.MakeId(number);
        }

        public SynchronizedStorageSession SynchronizedStorageSession => _ctx.SynchronizedStorageSession;

        public string MessageId => _ctx.MessageId;

        public string ReplyToAddress => _ctx.ReplyToAddress;

        public IReadOnlyDictionary<string, string> MessageHeaders => _ctx.MessageHeaders as IReadOnlyDictionary<string, string>;

        public ContextBag Extensions => _ctx.Extensions;

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
            return _ctx.HandleCurrentMessageLater();
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
    }
}
