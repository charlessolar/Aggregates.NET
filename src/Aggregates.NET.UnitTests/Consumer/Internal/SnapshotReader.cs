using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.Transport;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Consumer.Internal
{
    [TestFixture]
    public class SnapshotReader
    {
        private Moq.Mock<IEventStoreConsumer> _consumer;
        private Aggregates.Internal.SnapshotReader _subscriber;

        [SetUp]
        public void Setup()
        {
            _consumer = new Moq.Mock<IEventStoreConsumer>();
            var store = new Moq.Mock<IStoreEvents>();
            _subscriber = new Aggregates.Internal.SnapshotReader(store.Object, _consumer.Object);
            
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
        public async Task connects_to_snapshot_stream()
        {
            _consumer.Setup(
                x =>
                    x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>())).Returns(Task.FromResult(true));

            await _subscriber.Setup("test", CancellationToken.None, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            _consumer.Verify(
                x =>
                    x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                        Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                        Moq.It.IsAny<Func<Task>>()), Moq.Times.Once);
        }

        [Test]
        public async Task gets_snapshot()
        {

            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(1);
            eventCb("test", 0, message.Object);

            var snapshot = await _subscriber.Retreive("test").ConfigureAwait(false);
            Assert.AreEqual(1, snapshot.Payload);


            cts.Cancel();
        }

        [Test]
        public async Task get_no_snapshot()
        {
            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.Null(await _subscriber.Retreive("tester").ConfigureAwait(false));
        }

        [Test]
        public async Task new_snapshot_replaces_old()
        {
            Action<string, long, IFullEvent> eventCb = null;
            _consumer.Setup(
                    x =>
                        x.SubscribeToStreamEnd(Moq.It.Is<string>(m => m.EndsWith(StreamTypes.Snapshot)),
                            Moq.It.IsAny<CancellationToken>(), Moq.It.IsAny<Action<string, long, IFullEvent>>(),
                            Moq.It.IsAny<Func<Task>>()))
                .Callback<string, CancellationToken, Action<string, long, IFullEvent>, Func<Task>>(
                    (stream, token, onEvent, onDisconnect) =>
                    {
                        eventCb = onEvent;
                    })
                .Returns(Task.FromResult(true));

            var cts = new CancellationTokenSource();
            await _subscriber.Setup("test", cts.Token, Version.Parse("0.0.0")).ConfigureAwait(false);

            await _subscriber.Connect().ConfigureAwait(false);

            Assert.NotNull(eventCb);

            var message = new Moq.Mock<IFullEvent>();
            message.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            message.Setup(x => x.Event).Returns(1);
            eventCb("test", 0, message.Object);
            message.Setup(x => x.Event).Returns(2);
            eventCb("test", 0, message.Object);

            var snapshot = await _subscriber.Retreive("test").ConfigureAwait(false);
            Assert.AreEqual(2, snapshot.Payload);


            cts.Cancel();
        }

    }
}
