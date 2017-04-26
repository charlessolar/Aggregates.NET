using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using NServiceBus;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal.ConflictResolvers
{
    [TestFixture]
    public class WeakConflictMessageHandler
    {
        public class Entity : Aggregates.AggregateWithMemento<Entity, Entity.Memento>
        {
            public int Handles = 0;
            public int Conflicts = 0;
            public bool TakeASnapshot = false;

            public Entity(IEventStream stream, IRouteResolver resolver)
            {
                (this as INeedStream).Stream = stream;
                (this as INeedRouteResolver).Resolver = resolver;
            }

            public void Handle(IEvent e)
            {
                Handles++;
            }

            public void Conflict(IEvent e)
            {
                Conflicts++;
            }

            public class Memento : IMemento
            {
                public Id EntityId { get; set; }
            }

            protected override void RestoreSnapshot(Entity.Memento memento)
            {
            }

            protected override Entity.Memento TakeSnapshot()
            {
                return new Entity.Memento();
            }

            protected override bool ShouldTakeSnapshot()
            {
                return TakeASnapshot;
            }
        }

        public class Child : Aggregates.Entity<Child, Entity>
        {
            public int Handles = 0;
            public int Conflicts = 0;
            public Child(IEventStream stream, IRouteResolver resolver)
            {
                (this as INeedStream).Stream = stream;
                (this as INeedRouteResolver).Resolver = resolver;
            }

            public void Handle(IEvent e)
            {
                Handles++;
            }

            public void Conflict(IEvent e)
            {
                Conflicts++;
            }
        }
        class Event : IEvent { }

        private Moq.Mock<IUnitOfWork> _uow;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IRouteResolver> _resolver;
        private Moq.Mock<IStoreEvents> _eventstore;
        private Moq.Mock<IStoreStreams> _store;
        private Moq.Mock<IFullEvent> _event;
        private Moq.Mock<IDelayedChannel> _channel;
        private bool _wasFrozen;
        private Entity _entity;
        private ConflictingEvents _conflicting;

        [SetUp]
        public void Setup()
        {
            _uow = new Moq.Mock<IUnitOfWork>();
            _stream = new Moq.Mock<IEventStream>();
            _resolver = new Moq.Mock<IRouteResolver>();
            _eventstore = new Moq.Mock<IStoreEvents>();
            _store = new Moq.Mock<IStoreStreams>();
            _channel = new Moq.Mock<IDelayedChannel>();

            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event)))
                .Returns((entity, e) => (entity as Entity).Conflict((IEvent)e));
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Entity>(), typeof(Event)))
                .Returns((entity, e) => (entity as Entity).Handle((IEvent)e));
            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Child>(), typeof(Event)))
                .Returns((entity, e) => (entity as Child).Conflict((IEvent)e));
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Child>(), typeof(Event)))
                .Returns((entity, e) => (entity as Child).Handle((IEvent)e));

            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>())).Returns(Task.CompletedTask);

            _event = new Moq.Mock<IFullEvent>();
            _event.Setup(x => x.Event).Returns(new Event());


            _channel.Setup(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()))
                .Returns(Task.FromResult((TimeSpan?)TimeSpan.FromSeconds(60)));


            var repo = new Moq.Mock<IRepository<Entity>>();

            _entity = new Entity(_stream.Object, _resolver.Object);
            repo.Setup(x => x.Get(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>())).Returns(Task.FromResult(_entity));
            _uow.Setup(x => x.For<Entity>()).Returns(repo.Object);

            _eventstore.Setup(
                    x => x.WriteEvents("test", new[] { _event.Object }, Moq.It.IsAny<IDictionary<string, string>>(), null))
                .Returns(Task.FromResult(0L));

            _store.Setup(x => x.Freeze<Entity>(Moq.It.IsAny<IEventStream>())).Returns(Task.CompletedTask).Callback(() => _wasFrozen = true);
            _store.Setup(x => x.Unfreeze<Entity>(Moq.It.IsAny<IEventStream>())).Returns(Task.CompletedTask);

            _conflicting = new ConflictingEvents
            {
                EntityType = typeof(Entity).AssemblyQualifiedName,
                Parents = new Tuple<string, Id>[] { },
                Events = new[] { _event.Object }
            };
        }

        [TearDown]
        public void Teardown()
        {
            // Verify stream is always unfrozen
            if (_wasFrozen)
                _store.Verify(x => x.Unfreeze<Entity>(Moq.It.IsAny<IEventStream>()), Moq.Times.Once);
        }

        [Test]
        public async Task handle_conflicting_message()
        {
            var handler = new HandleConflictingEvents(_uow.Object);

            await handler.Handle(_conflicting, new Moq.Mock<IMessageHandlerContext>().Object).ConfigureAwait(false);

            Assert.AreEqual(1, _entity.Conflicts);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
            
        }

        [Test]
        public void no_route_exception()
        {
            var handler = new HandleConflictingEvents(_uow.Object);

            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event))).Throws<NoRouteException>();

            Assert.ThrowsAsync<ConflictResolutionFailedException>(
                () => handler.Handle(_conflicting, new Moq.Mock<IMessageHandlerContext>().Object));
        }
        

        [Test]
        public void dont_catch_abandon_resolution()
        {
            var handler = new HandleConflictingEvents(_uow.Object);

            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event))).Throws<AbandonConflictException>();

            Assert.ThrowsAsync<AbandonConflictException>(
                () => handler.Handle(_conflicting, new Moq.Mock<IMessageHandlerContext>().Object));
        }

        [Test]
        public async Task takes_snapshot()
        {
            var handler = new HandleConflictingEvents(_uow.Object);

            _stream.Setup(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.CommitVersion).Returns(1);

            _entity.TakeASnapshot = true;

            await handler.Handle(_conflicting, new Moq.Mock<IMessageHandlerContext>().Object)
                .ConfigureAwait(false);

            Assert.AreEqual(1, _entity.Conflicts);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
            
            _stream.Verify(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()), Moq.Times.Once);
        }

        [Test]
        public async Task handles_child_entity()
        {
            var child = new Child(_stream.Object, _resolver.Object);

            var repo = new Moq.Mock<IRepository<Entity, Child>>();
            
            repo.Setup(x => x.Get(Moq.It.IsAny<Id>())).Returns(Task.FromResult(child));
            _uow.Setup(x => x.For<Entity, Child>(Moq.It.IsAny<Entity>())).Returns(repo.Object);

            var handler = new HandleConflictingEvents(_uow.Object);

            var conflict = new ConflictingEvents
            {
                EntityType = typeof(Child).AssemblyQualifiedName,
                Parents = new Tuple<string, Id>[] { new Tuple<string, Id>(typeof(Entity).AssemblyQualifiedName, "test") },
                Events = new[] { _event.Object }
            };

            await handler.Handle(conflict, new Moq.Mock<IMessageHandlerContext>().Object).ConfigureAwait(false);

            Assert.AreEqual(1, child.Conflicts);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
        }
        
    }
}
