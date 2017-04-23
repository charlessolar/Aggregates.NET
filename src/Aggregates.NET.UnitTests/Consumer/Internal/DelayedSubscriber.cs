using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.NET.UnitTests.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;
using Newtonsoft.Json;
using NServiceBus.Transport;

namespace Aggregates.NET.UnitTests.Consumer.Internal
{
    [TestFixture]
    public class DelayedSubscriber
    {
        private Moq.Mock<IEventStoreConsumer> _consumer;
        private Aggregates.Internal.DelayedSubscriber _subscriber;
        private bool _onMessaged;

        [SetUp]
        public void Setup()
        {
            _consumer = new Moq.Mock<IEventStoreConsumer>();
            _subscriber = new Aggregates.Internal.DelayedSubscriber(_consumer.Object, 1);

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
        public async Task connects_to_delayed_domain_stream()
        {
            _consumer.Setup(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);
        }

        [Test]
        public async Task delayed_event_gets_processed()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test", 0, message.Object);
            
            Assert.That(() => _onMessaged, Is.True.After(300));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>()), Moq.Times.Once);

            cts.Cancel();
        }

        [Test]
        public async Task delayed_event_is_retried()
        {
            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());

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

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(2).After(1000).PollEvery(100));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>()), Moq.Times.Once);

            cts.Cancel();
        }

        [Test]
        public async Task delayed_event_retry_forever()
        {
            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            
            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                throw new Exception();
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.GreaterThan(2).After(1000).PollEvery(100));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>()), Moq.Times.Never);

            cts.Cancel();

        }

        [Test]
        public async Task canceled_event_isnt_retried()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());

            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                ctx.ReceiveCancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(1).After(1000).PollEvery(100));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>()), Moq.Times.Never);

            cts.Cancel();
        }
        [Test]
        public async Task canceled_thread_stops_retries()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());

            var called = 0;
            Bus.OnMessage = (ctx) =>
            {
                called++;
                cts.Cancel();
                throw new Exception();
            };

            eventCb("test", 0, message.Object);

            Assert.That(() => called, Is.EqualTo(1).After(1000).PollEvery(100));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>()), Moq.Times.Never);

            cts.Cancel();
        }

        [Test]
        public async Task process_events_sets_header()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test", 0, message.Object);

            var gotMessage = false;
            Bus.OnMessage = (ctx) =>
            {
                gotMessage = true;
                Assert.Contains(Defaults.BulkHeader, ctx.Headers.Keys);
                return Task.CompletedTask;
            };
            Assert.That(() => gotMessage, Is.True.After(1000).PollEvery(100));

            cts.Cancel();
        }

        [Test]
        public async Task multiple_events_same_stream_in_bulk()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test", 0, message.Object);
            eventCb("test", 0, message.Object);
            eventCb("test", 0, message.Object);
            eventCb("test", 0, message.Object);

            var gotMessage = 0;
            var delayedLength = 0;
            Bus.OnMessage = (ctx) =>
            {
                gotMessage++;
                IDelayedMessage[] delayed=null;
                ctx.Extensions.TryGet<IDelayedMessage[]>(Defaults.BulkHeader, out delayed);

                delayedLength = delayed?.Length ?? 0;
                return Task.CompletedTask;
            };
            Assert.That(() => gotMessage, Is.EqualTo(1).After(1000).PollEvery(100));
            Assert.AreEqual(4, delayedLength);

            cts.Cancel();
        }

        [Test]
        public async Task multiple_events_different_stream_not_bulk()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<IEnumerable<IFullEvent>>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test1", 0, message.Object);
            eventCb("test2", 0, message.Object);
            eventCb("test3", 0, message.Object);
            eventCb("test4", 0, message.Object);
            
            var gotMessage = 0;
            var delayedLength = 0;
            Bus.OnMessage = (ctx) =>
            {
                gotMessage++;
                IDelayedMessage[] delayed = null;
                ctx.Extensions.TryGet<IDelayedMessage[]>(Defaults.BulkHeader, out delayed);

                // Keep the largest sized delayed
                delayedLength = Math.Max(delayed?.Length ?? 0, delayedLength);
                return Task.CompletedTask;
            };
            Assert.That(() => gotMessage, Is.EqualTo(4).After(1000).PollEvery(100));
            Assert.AreEqual(1, delayedLength);

            cts.Cancel();
        }

        [Test]
        public async Task consumer_reconnects()
        {
            Func<Task> disconnect=null;

            _consumer.Setup(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()))
                        .Callback<string, string, CancellationToken, Action<string,long,IFullEvent>, Func<Task>>((stream, group, token, onEvent, dis) => disconnect=dis)
                        .Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);

            Assert.NotNull(disconnect);

            await disconnect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectPinnedPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Exactly(2));

        }
    }
}
