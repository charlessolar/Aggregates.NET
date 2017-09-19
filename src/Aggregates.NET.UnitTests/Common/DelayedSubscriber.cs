using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;
using Newtonsoft.Json;
using NServiceBus.Transport;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class DelayedSubscriber
    {
        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IEventStoreConsumer> _consumer;
        private Moq.Mock<IMessageDispatcher> _dispatcher;
        private Aggregates.Internal.DelayedSubscriber _subscriber;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _consumer = new Moq.Mock<IEventStoreConsumer>();
            _dispatcher = new Moq.Mock<IMessageDispatcher>();

            var fake = new FakeConfiguration();
            fake.FakeContainer.Setup(x => x.Resolve<IMetrics>()).Returns(_metrics.Object);
            fake.FakeContainer.Setup(x => x.Resolve<IEventStoreConsumer>()).Returns(_consumer.Object);
            fake.FakeContainer.Setup(x => x.Resolve<IMessageDispatcher>()).Returns(_dispatcher.Object);

            Configuration.Settings = fake;

            _dispatcher.Setup(x => x.SendLocal(Moq.It.IsAny<IFullMessage[]>(), Moq.It.IsAny<IDictionary<string, string>>())).Returns(Task.CompletedTask);

            _subscriber = new Aggregates.Internal.DelayedSubscriber(_metrics.Object, _consumer.Object, _dispatcher.Object, 1);
            
        }

        [TearDown]
        public void Teardown()
        {
            _subscriber.Dispose();
        }

        [Test]
        public async Task enables_by_category()
        {
            _consumer.Setup(x => x.EnableProjection("$by_category")).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            _consumer.Verify(x => x.EnableProjection("$by_category"), Moq.Times.Once);

        }

        [Test]
        public async Task connects_to_delayed_domain_stream()
        {
            _consumer.Setup(
                x =>
                    x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
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
                        x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test", 0, message.Object);


            Assert.That(() =>
            {
                // no idea how to do a Moq verify with a polling timer
                try
                {
                    _dispatcher.Verify(x => x.SendLocal(Moq.It.IsAny<IFullMessage[]>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
                }
                catch { return false; }
                return true;
            }, Is.EqualTo(true).After(1000).PollEvery(100));
            
            

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

            cts.Cancel();
        }

        //[Test]
        //public async Task delayed_event_is_retried()
        //{
        //    Action<string, long, IFullEvent> eventCb = null;
        //    _consumer.Setup(
        //            x =>
        //                x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
        //                    Moq.It.IsAny<string>(),
        //                    Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
        //                    Moq.It.IsAny<Func<Task>>()))
        //        .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
        //            (stream, group, token, onEvent, onDisconnect) =>
        //            {
        //                eventCb = onEvent;
        //            })
        //        .Returns(Task.FromResult(true));
        //    _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

        //    var cts = new CancellationTokenSource();
        //    await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

        //    await _subscriber.Connect().ConfigureAwait(false);

        //    Assert.NotNull(eventCb);

        //    var message = new Moq.Mock<IFullEvent>();
        //    message.Setup(x => x.Descriptor).Returns(new EventDescriptor());

        //    var threw = false;
        //    var called = 0;
        //    Bus.OnMessage = (ctx) =>
        //    {
        //        called++;
        //        if (threw)
        //            return Task.CompletedTask;

        //        threw = true;
        //        throw new Exception();
        //    };

        //    eventCb("test", 0, message.Object);

        //    Assert.That(() => called, Is.EqualTo(2).After(1000).PollEvery(100));

        //    _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Once);

        //    cts.Cancel();
        //}

        //[Test]
        //public async Task delayed_event_retry_forever()
        //{
        //    Action<string, long, IFullEvent> eventCb = null;
        //    _consumer.Setup(
        //            x =>
        //                x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
        //                    Moq.It.IsAny<string>(),
        //                    Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
        //                    Moq.It.IsAny<Func<Task>>()))
        //        .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
        //            (stream, group, token, onEvent, onDisconnect) =>
        //            {
        //                eventCb = onEvent;
        //            })
        //        .Returns(Task.FromResult(true));
        //    _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

        //    var cts = new CancellationTokenSource();
        //    await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

        //    await _subscriber.Connect().ConfigureAwait(false);

        //    Assert.NotNull(eventCb);

        //    var message = new Moq.Mock<IFullEvent>();
        //    message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            
        //    var called = 0;
        //    Bus.OnMessage = (ctx) =>
        //    {
        //        called++;
        //        throw new Exception();
        //    };

        //    eventCb("test", 0, message.Object);

        //    Assert.That(() => called, Is.GreaterThanOrEqualTo(10).After(10000).PollEvery(200));

        //    // Delayed events are acked immediately
        //    //_consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Never);

        //    cts.Cancel();

        //}

        // Delayed subscriber now acknowledges all events it gets immediately
        //[Test]
        //public async Task canceled_event_isnt_retried()
        //{

        //    Action<string, long, IFullEvent> eventCb = null;
        //    _consumer.Setup(
        //            x =>
        //                x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
        //                    Moq.It.IsAny<string>(),
        //                    Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
        //                    Moq.It.IsAny<Func<Task>>()))
        //        .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
        //            (stream, group, token, onEvent, onDisconnect) =>
        //            {
        //                eventCb = onEvent;
        //            })
        //        .Returns(Task.FromResult(true));
        //    _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

        //    var cts = new CancellationTokenSource();
        //    await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

        //    await _subscriber.Connect().ConfigureAwait(false);

        //    Assert.NotNull(eventCb);

        //    var message = new Moq.Mock<IFullEvent>();
        //    message.Setup(x => x.Descriptor).Returns(new EventDescriptor());

        //    var called = 0;
        //    Bus.OnMessage = (ctx) =>
        //    {
        //        called++;
        //        ctx.ReceiveCancellationTokenSource.Cancel();
        //        return Task.CompletedTask;
        //    };

        //    eventCb("test", 0, message.Object);

        //    Assert.That(() => called, Is.EqualTo(1).After(1000).PollEvery(100));

        //    _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Never);

        //    cts.Cancel();
        //}
        //[Test]
        //public async Task canceled_thread_stops_retries()
        //{

        //    Action<string, long, IFullEvent> eventCb = null;
        //    _consumer.Setup(
        //            x =>
        //                x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
        //                    Moq.It.IsAny<string>(),
        //                    Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
        //                    Moq.It.IsAny<Func<Task>>()))
        //        .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
        //            (stream, group, token, onEvent, onDisconnect) =>
        //            {
        //                eventCb = onEvent;
        //            })
        //        .Returns(Task.FromResult(true));
        //    _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

        //    var cts = new CancellationTokenSource();
        //    await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

        //    await _subscriber.Connect().ConfigureAwait(false);

        //    Assert.NotNull(eventCb);

        //    var message = new Moq.Mock<IFullEvent>();
        //    message.Setup(x => x.Descriptor).Returns(new EventDescriptor());

        //    var called = 0;
        //    Bus.OnMessage = (ctx) =>
        //    {
        //        called++;
        //        cts.Cancel();
        //        throw new Exception();
        //    };

        //    eventCb("test", 0, message.Object);

        //    Assert.That(() => called, Is.EqualTo(1).After(1000).PollEvery(100));

        //    // Delayed acks events immediately
        //   // _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Never);

        //    cts.Cancel();
        //}

        //[Test]
        //public async Task process_events_sets_header()
        //{

        //    Action<string, long, IFullEvent> eventCb = null;
        //    _consumer.Setup(
        //            x =>
        //                x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
        //                    Moq.It.IsAny<string>(),
        //                    Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
        //                    Moq.It.IsAny<Func<Task>>()))
        //        .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
        //            (stream, group, token, onEvent, onDisconnect) =>
        //            {
        //                eventCb = onEvent;
        //            })
        //        .Returns(Task.FromResult(true));
        //    _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

        //    var cts = new CancellationTokenSource();
        //    await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

        //    await _subscriber.Connect().ConfigureAwait(false);

        //    Assert.NotNull(eventCb);

        //    var message = new Moq.Mock<IFullEvent>();
        //    message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
        //    eventCb("test", 0, message.Object);

        //    var gotMessage = false;
        //    Bus.OnMessage = (ctx) =>
        //    {
        //        gotMessage = true;
        //        Assert.Contains(Defaults.BulkHeader, ctx.Headers.Keys);
        //        return Task.CompletedTask;
        //    };
        //    Assert.That(() => gotMessage, Is.True.After(1000).PollEvery(100));

        //    cts.Cancel();
        //}

        [Test]
        public async Task multiple_events_same_stream_in_bulk()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test", 0, message.Object);
            eventCb("test", 0, message.Object);
            eventCb("test", 0, message.Object);
            eventCb("test", 0, message.Object);


            Assert.That(() =>
            {
                // no idea how to do a Moq verify with a polling timer
                try
                {
                    _dispatcher.Verify(x => x.SendLocal(Moq.It.IsAny<IFullMessage[]>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
                }
                catch { return false; }
                return true;
            }, Is.EqualTo(true).After(1000).PollEvery(100));


            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Exactly(4));

            
            cts.Cancel();
        }

        [Test]
        public async Task multiple_events_different_stream_not_bulk()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                            Moq.It.IsAny<string>(),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, group, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));
            _consumer.Setup(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>())).Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            eventCb("test1", 0, message.Object);
            eventCb("test2", 0, message.Object);
            eventCb("test3", 0, message.Object);
            eventCb("test4", 0, message.Object);

            Assert.That(() =>
            {
                // no idea how to do a Moq verify with a polling timer
                try
                {
                    _dispatcher.Verify(x => x.SendLocal(Moq.It.IsAny<IFullMessage[]>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Exactly(4));
                }
                catch { return false; }
                return true;
            }, Is.EqualTo(true).After(1000).PollEvery(100));

            _consumer.Verify(x => x.Acknowledge(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IFullEvent>()), Moq.Times.Exactly(4));
            

            cts.Cancel();
        }

        [Test]
        public async Task consumer_reconnects()
        {
            Func<Task> disconnect=null;

            _consumer.Setup(
                x =>
                    x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()))
                        .Callback<string, string, CancellationToken, Action<string,long,IFullEvent>, Func<Task>>((stream, group, token, onEvent, dis) => disconnect=dis)
                        .Returns(Task.FromResult(true));

            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);

            Assert.NotNull(disconnect);

            await disconnect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.ConnectRoundRobinPersistentSubscription(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Delayed)),
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Exactly(2));

        }
    }
}
