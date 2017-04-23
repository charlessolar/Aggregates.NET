using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.MessageInterfaces;
using NServiceBus.Transport;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Consumer.Internal
{
    [TestFixture]
    public class EventSubscriber
    {
        private Moq.Mock<IEventStoreConsumer> _consumer;
        private Aggregates.Internal.EventSubscriber _subscriber;
        private bool _onMessaged;

        [SetUp]
        public void Setup()
        {
            _consumer = new Moq.Mock<IEventStoreConsumer>();

            var messaging = new Moq.Mock<IMessaging>();

            _subscriber = new Aggregates.Internal.EventSubscriber(messaging.Object, _consumer.Object, 1);

            Bus.OnMessage = (ctx) =>
            {
                _onMessaged = true;
                return Task.CompletedTask;
            };
            Bus.OnError = (ctx) => Task.FromResult(ErrorHandleResult.Handled);
        }

        [TearDown]
        public void Teardown()
        {
            _subscriber.Dispose();
            Bus.OnMessage = null;
            Bus.OnError = null;
        }

        [Test]
        public async Task enables_by_category()
        {
            _consumer.Setup(x => x.EnableProjection("$by_category")).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            _consumer.Verify(x => x.EnableProjection("$by_category"), Moq.Times.Once);

        }



        [Test]
        public async Task connects_to_app_stream()
        {
            _consumer.Setup(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);
        }
        [Test]
        public async Task connects_to_oob_stream()
        {
            _consumer.Setup(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.OOB)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.OOB)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);
        }
        [Test]
        public async Task connects_to_poco_stream()
        {
            _consumer.Setup(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Poco)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Poco)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);
        }


        [Test]
        public async Task event_gets_processed()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(new object());

            eventCb("test", 0, message.Object);

            Assert.That(() => _onMessaged, Is.True.After(300));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

            cts.Cancel();
        }

        [Test]
        public async Task event_is_retried()
        {
            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(new object());

            var threw = false;
            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                if (threw)
                    return Task.CompletedTask;

                threw = true;
                throw new Exception();
            };
            Bus.OnError = (ctx) =>
            {
                return Task.FromResult(ErrorHandleResult.RetryRequired);
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(2).After(1000).PollEvery(100));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

            cts.Cancel();
        }

        [Test]
        public async Task event_retry_max_times()
        {
            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(new object());

            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                throw new Exception();
            };
            Bus.OnError = (ctx) =>
            {
                if (called == 2)
                    return Task.FromResult(ErrorHandleResult.Handled);
                return Task.FromResult(ErrorHandleResult.RetryRequired);
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(2).After(1000).PollEvery(100));

            // Even failed events are acknowledged because they are sent to the error queue
            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

            cts.Cancel();

        }

        [Test]
        public async Task canceled_event_isnt_retried()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(new object());

            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                ctx.ReceiveCancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(1).After(1000).PollEvery(100));

            // Even failed events are acknowledged because they are sent to the error queue
            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

            cts.Cancel();
        }
        [Test]
        public async Task canceled_thread_stops_retries()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(new object());

            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                cts.Cancel();
                throw new Exception();
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(1).After(1000).PollEvery(100));

            // Even failed events are acknowledged because they are sent to the error queue
            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

            cts.Cancel();
        }
        [Test]
        public async Task consumer_reconnects()
        {
            Func<Task> disconnect = null;

            _consumer.Setup(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()))
                        .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>((stream, group, token, onEvent, dis) => disconnect = dis)
                        .Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);

            Assert.NotNull(disconnect);

            await disconnect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Domain)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Exactly(2));

        }
    }
}
