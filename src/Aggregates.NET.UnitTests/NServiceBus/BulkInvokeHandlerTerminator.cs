using Aggregates.Contracts;
using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Sagas;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.Reflection;

namespace Aggregates.NServiceBus
{
    public class BulkInvokeHandlerTerminator : TestSubject<Internal.BulkInvokeHandlerTerminator>
    {
        [Fact]
        public async Task SagaIsFoundProcessMessage()
        {
            var handlerInvoked = false;
            var saga = new FakeSaga();

            var messageHandler = CreateMessageHandler((i, m, ctx) => handlerInvoked = true, saga);
            var behaviorContext = CreateBehaviorContext(messageHandler);
            AssociateSagaWithMessage(saga, behaviorContext);

            await Sut.Invoke(behaviorContext, _ => Task.CompletedTask);

            handlerInvoked.Should().BeTrue();
        }
        [Fact]
        public async Task SagaNotFoundAndHandlerIsSaga()
        {
            var handlerInvoked = false;
            var saga = new FakeSaga();

            var messageHandler = CreateMessageHandler((i, m, ctx) => handlerInvoked = true, saga);
            var behaviorContext = CreateBehaviorContext(messageHandler);
            var sagaInstance = AssociateSagaWithMessage(saga, behaviorContext);
            sagaInstance.GetType().GetMethod("MarkAsNotFound", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sagaInstance, new object[] { });

            await Sut.Invoke(behaviorContext, _ => Task.CompletedTask);

            handlerInvoked.Should().BeFalse();
        }
        [Fact]
        public async Task SagaNotFoundAndHandlerNotSaga()
        {
            var handlerInvoked = false;

            var messageHandler = CreateMessageHandler((i, m, ctx) => handlerInvoked = true, new FakeMessageHandler());
            var behaviorContext = CreateBehaviorContext(messageHandler);
            var sagaInstance = AssociateSagaWithMessage(new FakeSaga(), behaviorContext);
            sagaInstance.GetType().GetMethod("MarkAsNotFound", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sagaInstance, new object[] { });

            await Sut.Invoke(behaviorContext, _ => Task.CompletedTask);

            handlerInvoked.Should().BeTrue();
        }
        [Fact]
        public async Task NoSaga()
        {
            var handlerInvoked = false;

            var messageHandler = CreateMessageHandler((i, m, ctx) => handlerInvoked = true, new FakeMessageHandler());
            var behaviorContext = CreateBehaviorContext(messageHandler);

            await Sut.Invoke(behaviorContext, _ => Task.CompletedTask);

            handlerInvoked.Should().BeTrue();
        }
        [Fact]
        public async Task InvokeHandlerWithCurrentMessage()
        {
            object receivedMessage = null;
            var messageHandler = CreateMessageHandler((i, m, ctx) => receivedMessage = m, new FakeMessageHandler());
            var behaviorContext = CreateBehaviorContext(messageHandler);

            await Sut.Invoke(behaviorContext, _ => Task.CompletedTask);

            behaviorContext.MessageBeingHandled.Should().BeSameAs(receivedMessage);
        }


        static ActiveSagaInstance AssociateSagaWithMessage(FakeSaga saga, IInvokeHandlerContext behaviorContext)
        {
            var sagaInstance = new ActiveSagaInstance(saga, SagaMetadata.Create(typeof(FakeSaga), new List<Type>(), new Conventions()), () => DateTime.UtcNow);
            behaviorContext.Extensions.Set(sagaInstance);
            return sagaInstance;
        }

        static MessageHandler CreateMessageHandler(Action<object, object, IMessageHandlerContext> invocationAction, object handlerInstance)
        {
            var messageHandler = new MessageHandler((instance, message, handlerContext) =>
            {
                invocationAction(instance, message, handlerContext);
                return Task.CompletedTask;
            }, handlerInstance.GetType())
            {
                Instance = handlerInstance
            };
            return messageHandler;
        }

        static MessageHandler CreateMessageHandlerThatReturnsNull(Action<object, object, IMessageHandlerContext> invocationAction, object handlerInstance)
        {
            var messageHandler = new MessageHandler((instance, message, handlerContext) =>
            {
                invocationAction(instance, message, handlerContext);
                return null;
            }, handlerInstance.GetType())
            {
                Instance = handlerInstance
            };
            return messageHandler;
        }

        static IInvokeHandlerContext CreateBehaviorContext(MessageHandler messageHandler)
        {
            var behaviorContext = new TestableInvokeHandlerContext
            {
                MessageHandler = messageHandler
            };

            return behaviorContext;
        }

        class FakeSaga : global::NServiceBus.Saga<FakeSaga.FakeSagaData>, IAmStartedByMessages<StartMessage>
        {
            public Task Handle(StartMessage message, IMessageHandlerContext context)
            {
                throw new NotImplementedException();
            }

            protected override void ConfigureHowToFindSaga(SagaPropertyMapper<FakeSagaData> mapper)
            {
                mapper.ConfigureMapping<StartMessage>(msg => msg.SomeId).ToSaga(saga => saga.SomeId);
            }

            public class FakeSagaData : ContainSagaData
            {
                public string SomeId { get; set; }
            }
        }

        class StartMessage
        {
            public string SomeId { get; set; }
        }

        class FakeMessageHandler
        {
        }
    }
}
