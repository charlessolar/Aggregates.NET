using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Aggregates.Messages;
using Aggregates.Internal;
using Aggregates.Contracts;
using Aggregates.Exceptions;

namespace Aggregates.UnitTests.Common.ConflictResolvers
{
    [TestFixture]
    public class EasyConflictResolvers
    {
        class FakeState : Aggregates.State<FakeState> {
            private void Handle(Event e) { }
        }

        class FakeEntity : Aggregates.Entity<FakeEntity, FakeState>
        {
            public FakeEntity()
            {
                Id = "test";
                State = new FakeState();
            }
        
        }
        class Event : IEvent { }

        private Moq.Mock<IEventMapper> _mapper;
        private Moq.Mock<IStoreEvents> _store;

        [SetUp]
        public void Setup()
        {
            _store = new Moq.Mock<IStoreEvents>();
            _mapper = new Moq.Mock<IEventMapper>();

            _mapper.Setup(x => x.GetMappedTypeFor(typeof(Event))).Returns(typeof(Event));

            var fake = new FakeConfiguration();
            fake.FakeContainer.Setup(x => x.Resolve<IEventMapper>()).Returns(_mapper.Object);
            Configuration.Settings = fake;
        }

        [Test]
        public void throw_resolver()
        {
            // Does not resolve, just throws
            var resolver = new ThrowConflictResolver();

            var fullevent = new Moq.Mock<IFullEvent>();
            fullevent.Setup(x => x.Event).Returns(new Event());

            var entity = new FakeEntity();
            Assert.ThrowsAsync<ConflictResolutionFailedException>(
                () => resolver.Resolve<FakeEntity, FakeState>(entity, new[] { fullevent.Object }, Guid.NewGuid(), new Dictionary<string, string>()));

        }

        [Test]
        public async Task ignore_resolver()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            var fullevent = new Moq.Mock<IFullEvent>();
            fullevent.Setup(x => x.Event).Returns(new Event());

            _store.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));

            // Ignores conflict, just commits
            var resolver = new IgnoreConflictResolver(_store.Object, streamGen);

            var entity = new FakeEntity();


            await resolver.Resolve<FakeEntity, FakeState>(entity, new[] {fullevent.Object}, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            _store.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
           
        }

        [Test]
        public async Task discard_resolver()
        {
            var store = new Moq.Mock<IStoreEvents>();

            var fullevent = new Moq.Mock<IFullEvent>();
            fullevent.Setup(x => x.Event).Returns(new Event());

            _store.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));
            store.Setup(
                    x => x.WriteEvents("test", new[] { fullevent.Object }, Moq.It.IsAny<IDictionary<string, string>>(), null))
                .Returns(Task.FromResult(0L));

            // Discards all conflicted events, doesn't save
            var resolver = new DiscardConflictResolver();

            var entity = new FakeEntity();

            await resolver.Resolve<FakeEntity, FakeState>(entity, new[] { fullevent.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            _store.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Never);
            store.Verify(
                x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), null),
                Moq.Times.Never);
        }


    }
}
