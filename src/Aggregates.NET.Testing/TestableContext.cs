using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Persistence;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public class TestableContext : IMessageHandlerContext
    {

        public readonly ITestableDomain UoW;
        public readonly ITestableApplication App;
        public readonly ITestableProcessor Processor;
        private readonly TestableMessageHandlerContext _ctx;
        private readonly IdRegistry _ids;

        public TestableContext()
        {
            _ids = new IdRegistry();
            _ctx = new TestableMessageHandlerContext();

            UoW = new TestableDomain(_ids);
            App = new TestableApplication(_ids);
            Processor = new TestableProcessor();

            _ctx.Extensions.Set<UnitOfWork.IDomain>(UoW);
            _ctx.Extensions.Set<UnitOfWork.IApplication>(App);
            _ctx.Extensions.Set<IProcessor>(Processor);
            _ctx.Extensions.Set<IContainer>(new TestableContainer());
        }

        public TEvent Create<TEvent>(Action<TEvent> action) where TEvent : Messages.IEvent
        {
            return Test.CreateInstance<TEvent>(action);
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

        public RepliedMessage<object>[] RepliedMessages => _ctx.RepliedMessages;
        public string[] ForwardedMessages => _ctx.ForwardedMessages;
        public SentMessage<object>[] SentMessages => _ctx.SentMessages;
    }
}
