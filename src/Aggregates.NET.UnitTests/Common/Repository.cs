using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Aggregates.Exceptions;
using Aggregates.Contracts;
using Aggregates.Attributes;
using Aggregates.Messages;
using Aggregates.Internal;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class Repository
    {
        class FakeResolver : IResolveConflicts
        {
            public bool Fail { get; set; }
            public bool Abandon { get; set; }
            public bool Called { get; set; }
            public IDictionary<string,string> CommitHeaders { get; set; }

            public Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId,
                IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new()
            {
                Called = true;

                if (Fail)
                    throw new ConflictResolutionFailedException();
                if (Abandon)
                    throw new AbandonConflictException();
                CommitHeaders = commitHeaders;

                return Task.CompletedTask;
            }
        }
        public class FakeState : State<FakeState>
        {
            public Id EntityId { get; set; }
            public string Test { get; set; }

            public bool TestTakeSnapshot { get; set; }

            private void Handle(FakeEvent e) { }
            
            protected override bool ShouldSnapshot()
            {
                return TestTakeSnapshot;
            }
        }
        [OptimisticConcurrency(ConcurrencyConflict.Custom, resolver: typeof(FakeResolver))]
        class FakeEntity : Entity<FakeEntity, FakeState>
        {
            private FakeEntity() { }
            public string Foo { get; set; }

        }

        class FakeEvent : IEvent { }

        class BadEntity : Entity<BadEntity, FakeState> {}

        private Aggregates.Internal.Repository<FakeEntity, FakeState> _repository;
        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IStoreSnapshots> _snapshots;
        private Moq.Mock<IStoreEvents> _eventstore;
        private Moq.Mock<IOobWriter> _oobStore;
        private Moq.Mock<IEventFactory> _factory;
        private Moq.Mock<IDomainUnitOfWork> _uow;
        private Moq.Mock<IFullEvent> _event;
        private Moq.Mock<IEventMapper> _mapper;
        private FakeResolver _resolver;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _snapshots = new Moq.Mock<IStoreSnapshots>();
            _eventstore = new Moq.Mock<IStoreEvents>();
            _oobStore = new Moq.Mock<IOobWriter>();
            _factory = new Moq.Mock<IEventFactory>();
            _uow = new Moq.Mock<IDomainUnitOfWork>();
            _event = new Moq.Mock<IFullEvent>();
            _mapper = new Moq.Mock<IEventMapper>();

            _mapper.Setup(x => x.GetMappedTypeFor(typeof(FakeEvent))).Returns(typeof(FakeEvent));
            _resolver = new FakeResolver();

            var fake = new FakeConfiguration();
            fake.FakeContainer.Setup(x => x.Resolve<IEventMapper>()).Returns(_mapper.Object);
            fake.FakeContainer.Setup(x => x.Resolve<IMetrics>()).Returns(_metrics.Object);
            fake.FakeContainer.Setup(x => x.Resolve(typeof(FakeResolver))).Returns(_resolver);
            fake.FakeContainer.Setup(x => x.Resolve<IStoreSnapshots>()).Returns(_snapshots.Object);
            fake.FakeContainer.Setup(x => x.Resolve<IStoreEvents>()).Returns(_eventstore.Object);

            Configuration.Settings = fake;

            _snapshots.Setup(x => x.GetSnapshot<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>()))
                .Returns(Task.FromResult((ISnapshot)null));

            _event.Setup(x => x.Event).Returns(new FakeEvent());
            _repository = new Aggregates.Internal.Repository<FakeEntity, FakeState>(_metrics.Object, _eventstore.Object, _snapshots.Object, _oobStore.Object, _factory.Object, _uow.Object);
        }

        [Test]
        public async Task get_exists()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));
            

            Assert.NotNull(await _repository.Get("test"));
        }

        [Test]
        public async Task get_exists_with_bucket()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>("test", new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            Assert.NotNull(await _repository.Get("test", "test"));
        }

        [Test]
        public void get_doesnt_exist()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Throws<NotFoundException>();

            Assert.ThrowsAsync<NotFoundException>(() => _repository.Get("test"));
        }
        [Test]
        public void try_get_no_exception()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Throws<NotFoundException>();

            Assert.DoesNotThrowAsync(() => _repository.TryGet("test"));
        }

        [Test]
        public void get_with_bucket_doesnt_exist()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>("test", new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Throws<NotFoundException>();

            Assert.ThrowsAsync<NotFoundException>(() => _repository.Get("test", "test"));
        }
        [Test]
        public void try_get_with_bucket_no_exception()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>("test", new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Throws<NotFoundException>();

            Assert.DoesNotThrowAsync(() => _repository.TryGet("test", "test"));
        }

        [Test]
        public async Task get_already_gotten()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));
            
            var entity = await _repository.Get("test");
            entity.Foo = "test";

            var entity2 = await _repository.Get("test");

            Assert.AreEqual(entity2.Foo, entity.Foo);

            _eventstore.Verify(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }
        

        [Test]
        public async Task new_stream()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()));

            var entity = await _repository.New("test");

            Assert.AreEqual(EntityFactory.NewEntityVersion, entity.Version);
            _eventstore.Verify(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Never);
        }
        [Test]
        public async Task new_with_bucket_stream()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>("test", new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()));

            var entity = await _repository.New("test", "test");

            Assert.AreEqual(EntityFactory.NewEntityVersion, entity.Version);
            _eventstore.Verify(x => x.GetEvents<FakeEntity>("test", new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Never);
        }

        [Test]
        public async Task verify_version_no_dirty()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");
            
            _eventstore.Setup(x => x.VerifyVersion<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>()))
                .Returns(Task.FromResult(true));
            
            await (_repository as IRepository).Prepare(Guid.NewGuid());

            _eventstore.Verify(x => x.VerifyVersion<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>()), Moq.Times.Once);
        }

        [Test]
        public async Task verify_version_dirty()
        {

            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");

            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            _eventstore.Setup(x => x.VerifyVersion<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>()))
                .Returns(Task.FromResult(true));

            await (_repository as IRepository).Prepare(Guid.NewGuid());

            _eventstore.Verify(x => x.VerifyVersion<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>()), Moq.Times.Never);
            
        }

        [Test]
        public async Task verify_version_throws_version_exception()
        {

            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");

            _eventstore.Setup(x => x.VerifyVersion<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>()))
                .Throws<VersionException>();

            Assert.ThrowsAsync<VersionException>(() => (_repository as IRepository).Prepare(Guid.NewGuid()));

            _eventstore.Verify(x => x.VerifyVersion<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>()), Moq.Times.Once);
            
        }

        [Test]
        public void commit_no_streams()
        {
            Assert.DoesNotThrowAsync(
                () => (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));
        }

        [Test]
        public async Task commit_stream()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string,string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

        }
        [Test]
        public async Task commit_stream_not_dirty()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");

            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Never);

        }

        [Test]
        public async Task commit_take_snapshot_but_not_changed()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");

            entity.State.TestTakeSnapshot = true;

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _snapshots.Verify(x => x.WriteSnapshots<FakeEntity>(Moq.It.IsAny<IState>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            
        }

        [Test]
        public async Task commit_take_snapshot()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");

            entity.State.TestTakeSnapshot = true;
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _snapshots.Verify(x => x.WriteSnapshots<FakeEntity>(Moq.It.IsAny<IState>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
            
        }

        [Test]
        public async Task commit_version_exception_new_stream_doesnt_start_resolution()
        {
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new VersionException("test"));

            var entity = await _repository.New("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.ThrowsAsync<ConflictResolutionFailedException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

            Assert.IsFalse(_resolver.Called);
        }

        [Test]
        public async Task commit_version_exception_starts_resolution()
        {
            _resolver.Fail = true;

            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new VersionException("test"));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.ThrowsAsync<ConflictResolutionFailedException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

            Assert.IsTrue(_resolver.Called);

        }

        [Test]
        public async Task commit_version_exception_resolution_succeeds()
        {
            _resolver.Fail = false;

            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new VersionException("test"));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.DoesNotThrowAsync(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

            Assert.IsTrue(_resolver.Called);

        }
        [Test]
        public async Task commit_version_exception_sets_header()
        {
            _resolver.Fail = false;

            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new VersionException("test"));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.DoesNotThrowAsync(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

            Assert.IsTrue(_resolver.Called);
            Assert.IsTrue(_resolver.CommitHeaders.ContainsKey(Defaults.ConflictResolvedHeader));
        }
        [Test]
        public async Task commit_version_exception_resolution_throws_abandon()
        {

            _resolver.Abandon = true;

            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new VersionException("test"));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.ThrowsAsync<ConflictResolutionFailedException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

            Assert.IsTrue(_resolver.Called);
        }


        [Test]
        public async Task commit_persistence_exception()
        {

            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new PersistenceException("test"));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.ThrowsAsync<PersistenceException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
            

        }
        [Test]
        public async Task commit_duplicate_commit_exception()
        {
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Throws(new DuplicateCommitException("test"));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());

            Assert.DoesNotThrowAsync(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
            
        }
        [Test]
        public async Task commit_oob_events_handled()
        {
            _eventstore.Setup(x => x.GetEvents<FakeEntity>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] { _event.Object }));

            var entity = await _repository.Get("test");
            // make entity dirty
            (entity as IEntity<FakeState>).Apply(new FakeEvent());
            (entity as IEntity<FakeState>).Raise(new FakeEvent(), "test");

            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));
            _oobStore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);

            _oobStore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }
    }
}
