using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Aggregates.Contracts;
using Aggregates.Messages;
using Aggregates.Exceptions;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class Entity
    {
        class Test : IEvent { }

        class FakeState : Aggregates.State<FakeState>
        {
            public int Handles;
            public int Conflicts;
            public bool Discard = false;

            private void Handle(Test e)
            {
                Handles++;
            }
            private void Conflict(Test e)
            {
                Conflicts++;

                if (Discard)
                    throw new DiscardEventException();
            }
        }

        class FakeEntity : Aggregates.Entity<FakeEntity, FakeState>
        {
            public FakeEntity(IEventFactory factory)
            {
                Id = "test";
                State = new FakeState();
                (this as INeedContainer).Container = Configuration.Settings.Container;
                (this as INeedEventFactory).EventFactory = factory;
            }
            
        }

        private Moq.Mock<IDomainUnitOfWork> _uow;
        private Moq.Mock<IEventFactory> _factory;
        private Moq.Mock<IStoreEvents> _eventstore;
        private FakeEntity _entity;



        [SetUp]
        public void Setup()
        {
            _uow = new Moq.Mock<IDomainUnitOfWork>();
            _factory = new Moq.Mock<IEventFactory>();
            _eventstore = new Moq.Mock<IStoreEvents>();

            var fake = new FakeConfiguration();
            fake.FakeContainer.Setup(x => x.Resolve<IStoreEvents>()).Returns(_eventstore.Object);

            Configuration.Build(fake).Wait();

            _entity = new FakeEntity(_factory.Object);
        }
        

        [Test]
        public async Task events_get_event()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { }));

            await _entity.GetEvents(0, 1).ConfigureAwait(false);

            _eventstore.Verify(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);

        }
        [Test]
        public async Task events_get_oobevent()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { }));
            
            await _entity.GetEvents(0, 1, oob: "test").ConfigureAwait(false);

            _eventstore.Verify(x => x.GetEvents<FakeEntity>("OOB-test", Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }
        [Test]
        public async Task events_size()
        {
            _eventstore.Setup(x => x.Size<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>()))
                .Returns(Task.FromResult(0L));

            await _entity.GetSize();

            _eventstore.Verify(x => x.Size<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>()), Moq.Times.Once);
        }

        [Test]
        public async Task events_get_event_backward()
        {
            _eventstore.Setup(x => x.GetEventsBackwards<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { }));

            await _entity.GetEventsBackwards(0, 1).ConfigureAwait(false);

            _eventstore.Verify(x => x.GetEventsBackwards<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);

        }
        [Test]
        public async Task events_get_oobevent_backward()
        {
            _eventstore.Setup(x => x.GetEventsBackwards<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { }));

            await _entity.GetEventsBackwards(0, 1, oob: "test").ConfigureAwait(false);

            _eventstore.Verify(x => x.GetEventsBackwards<FakeEntity>("OOB-test", Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }
        [Test]
        public void get_hash_code()
        {
            Assert.AreEqual(new Id("test").GetHashCode(), _entity.GetHashCode());
        }

        [Test]
        public void apply_is_dirty()
        {
            (_entity as IEntity<FakeState>).Apply(new Test());

            Assert.IsTrue(_entity.Dirty);
        }
        [Test]
        public void raise_is_dirty()
        {
            (_entity as IEntity<FakeState>).Raise(new Test(), "test");

            Assert.IsTrue(_entity.Dirty);
        }
        
    }
}
