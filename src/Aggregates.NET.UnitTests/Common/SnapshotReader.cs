using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using Newtonsoft.Json;
using NServiceBus.Transport;
using NUnit.Framework;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class SnapshotReader
    {
        class FakeState : Aggregates.State<FakeState> { }
        
        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IEventStoreConsumer> _consumer;
        private Aggregates.Internal.SnapshotReader _subscriber;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _consumer = new Moq.Mock<IEventStoreConsumer>();

            var factory = new Moq.Mock<IEventFactory>();
            var mapper = new Moq.Mock<IEventMapper>();
            var serializer = new JsonMessageSerializer(mapper.Object, factory.Object, new JsonConverter[] { });

            var store = new Moq.Mock<IStoreEvents>();
            
            _subscriber = new Aggregates.Internal.SnapshotReader(_metrics.Object, store.Object, _consumer.Object, serializer);
            Bus.BusOnline = true;

        }

        [TearDown]
        public void Teardown()
        {
            _subscriber.Dispose();
            Bus.OnMessage = null;
            Bus.OnError = null;
            Bus.BusOnline = false;
        }

        [Test]
        public async Task enables_by_category()
        {
            _consumer.Setup(x => x.EnableProjection("$by_category")).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            _consumer.Verify(x => x.EnableProjection("$by_category"), Moq.Times.Once);
        }
        [Test]
        public async Task connects_to_snapshot_stream()
        {
            _consumer.Setup(
                x =>
                    x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);
        }

        [Test]
        public async Task gets_snapshot()
        {

            Func<string, long, IFullEvent, Task> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, CancellationToken, Func<string, long, IFullEvent, Task>, Func<Task>>(
                    (stream, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var memento = new FakeState();
            memento.Version = 1;

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(memento);
            await eventCb("test", 0, message.Object).ConfigureAwait(false);

            var read = await _subscriber.Retreive("test").ConfigureAwait(false);
            Assert.AreEqual(1, (read.Payload as IState).Version);


            cts.Cancel();
        }
        [Test]
        public async Task gets_snapshot_is_clone()
        {

            Func<string, long, IFullEvent, Task> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, CancellationToken, Func<string, long, IFullEvent, Task>, Func<Task>>(
                    (stream, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var memento = new FakeState();
            memento.Version = 1;

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(memento);
            await eventCb("test", 0, message.Object).ConfigureAwait(false);

            var read = await _subscriber.Retreive("test").ConfigureAwait(false);

            // A change to the base memento shouldn't change what we get from snapshot reader (it should have cloned the snapshot)
            memento.Version = 2;
            Assert.AreEqual(1, (read.Payload as IState).Version);

            cts.Cancel();
        }

        [Test]
        public async Task get_no_snapshot()
        {
            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.Null(await _subscriber.Retreive("tester").ConfigureAwait(false));
        }

        [Test]
        public async Task new_snapshot_replaces_old()
        {
            Func<string, long, IFullEvent, Task> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, CancellationToken, Func<string, long, IFullEvent, Task>, Func<Task>>(
                    (stream, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var memento = new FakeState();
            var memento2 = new FakeState();
            memento.Version = 1;
            memento2.Version = 2;

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(memento);
            await eventCb("test", 0, message.Object).ConfigureAwait(false);
            message.Setup(x => x.Event).Returns(memento2);
            await eventCb("test", 0, message.Object).ConfigureAwait(false);

            var snapshot = await _subscriber.Retreive("test").ConfigureAwait(false);
            Assert.AreEqual(2, (snapshot.Payload as IState).Version);


            cts.Cancel();
        }

        [Test]
        public async Task consumer_reconnects()
        {
            Func<Task> disconnect = null;

            _consumer.Setup(
                    x =>
                        x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, CancellationToken, Func<string, long, IFullEvent, Task>, Func<Task>>(
                    (stream, token, onEvent, onDisconnect) =>
                    {
                        disconnect = onDisconnect;
                    })
                .Returns(Task.FromResult(true));

            await _subscriber.Setup("test", Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                            Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);

            Assert.NotNull(disconnect);

            await disconnect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Func<string, long, IFullEvent, Task>>(),
                            Moq.It.IsAny<Func<Task>>()), Moq.Times.Exactly(2));

        }
    }
}
