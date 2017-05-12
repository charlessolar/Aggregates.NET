using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class Repository
    {
        class FakeResolver : IResolveConflicts
        {
            public bool Fail { get; set; }
            public bool Abandon { get; set; }

            public Task Resolve<T>(T entity, IEnumerable<IFullEvent> uncommitted, Guid commitId,
                IDictionary<string, string> commitHeaders) where T : class, IEventSource
            {
                if (Fail)
                    throw new ConflictResolutionFailedException();
                if (Abandon)
                    throw new AbandonConflictException();

                return Task.CompletedTask;
            }
        }
        [OptimisticConcurrency(ConcurrencyConflict.Custom, resolver: typeof(FakeResolver))]
        class Entity : AggregateWithMemento<Entity, Entity.Memento>
        {
            private Entity() { }

            public string Foo { get; set; }
            public bool TestTakeSnapshot { get; set; }

            protected override void RestoreSnapshot(Memento memento)
            {
            }

            protected override bool ShouldTakeSnapshot()
            {
                return TestTakeSnapshot;
            }

            protected override Memento TakeSnapshot()
            {
                return new Memento {EntityId = Id};
            }

            public class Memento : IMemento
            {
                public Id EntityId { get; set; }
                public string Test { get; set; }
            }
        }

        class BadEntity : Aggregate<BadEntity> { }

        private Aggregates.Internal.Repository<Entity> _repository;
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreSnapshots> _snapshots;
        private Moq.Mock<IStoreStreams> _streams;
        private Moq.Mock<IEventStream> _stream;
        private FakeResolver _resolver;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _snapshots = new Moq.Mock<IStoreSnapshots>();
            _streams = new Moq.Mock<IStoreStreams>();
            _stream = new Moq.Mock<IEventStream>();
            _resolver = new FakeResolver();

            _builder.Setup(x => x.Build<IStoreSnapshots>()).Returns(_snapshots.Object);
            _builder.Setup(x => x.Build<IStoreStreams>()).Returns(_streams.Object);
            _builder.Setup(x => x.Build(typeof(FakeResolver))).Returns(_resolver);

            _repository = new Aggregates.Internal.Repository<Entity>(_builder.Object);
        }

        [Test]
        public async Task get_exists()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            Assert.NotNull(await _repository.Get("test"));
        }

        [Test]
        public async Task get_exists_with_bucket()
        {
            _streams.Setup(x => x.GetStream<Entity>("test", new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            Assert.NotNull(await _repository.Get("test", "test"));
        }

        [Test]
        public void get_doesnt_exist()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Throws<NotFoundException>();

            Assert.ThrowsAsync<NotFoundException>(() => _repository.Get("test"));
        }
        [Test]
        public void try_get_no_exception()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Throws<NotFoundException>();

            Assert.DoesNotThrowAsync(() => _repository.TryGet("test"));
        }

        [Test]
        public void get_with_bucket_doesnt_exist()
        {
            _streams.Setup(x => x.GetStream<Entity>("test", new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Throws<NotFoundException>();

            Assert.ThrowsAsync<NotFoundException>(() => _repository.Get("test", "test"));
        }
        [Test]
        public void try_get_with_bucket_no_exception()
        {
            _streams.Setup(x => x.GetStream<Entity>("test", new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Throws<NotFoundException>();

            Assert.DoesNotThrowAsync(() => _repository.TryGet("test", "test"));
        }

        [Test]
        public async Task get_already_gotten()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            var entity = await _repository.Get("test");
            entity.Foo = "test";

            var entity2 = await _repository.Get("test");

            Assert.AreEqual(entity2.Foo, entity.Foo);

            _streams.Verify(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>()), Moq.Times.Once);
        }

        [Test]
        public async Task get_has_snapshot()
        {
            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(100);

            _snapshots.Setup(x => x.GetSnapshot<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(snapshot.Object));
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.Is<ISnapshot>(s => s.Version==100))).Returns(Task.FromResult(_stream.Object));

            var entity = await _repository.Get("test");

            _streams.Verify(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.Is<ISnapshot>(s => s.Version == 100)), Moq.Times.Once);
        }

        [Test]
        public async Task get_restores_snapshot()
        {
            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(100);
            snapshot.Setup(x => x.Payload).Returns(new Entity.Memento {EntityId = "test", Test = "Foo"});
            
            _stream.Setup(x => x.Snapshot).Returns(snapshot.Object);
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            var entity = await _repository.Get("test");

            Assert.AreEqual("Foo", entity.Snapshot.Test);
        }

        [Test]
        public async Task new_stream()
        {
            _streams.Setup(x => x.NewStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(_stream.Object));

            var entity = await _repository.New("test");

            Assert.AreEqual(0, entity.CommitVersion);
        }
        [Test]
        public async Task new_with_bucket_stream()
        {
            _streams.Setup(x => x.NewStream<Entity>("test", new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(_stream.Object));

            var entity = await _repository.New("test", "test");

            Assert.AreEqual(0, entity.CommitVersion);
        }

        [Test]
        public void bad_entity_throws()
        {
            _streams.Setup(x => x.NewStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(_stream.Object));
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            var repository = new Aggregates.Internal.Repository<BadEntity>(_builder.Object);
            Assert.ThrowsAsync<Aggregates.Exceptions.AggregateException>(() => repository.New("test"));
            Assert.ThrowsAsync<Aggregates.Exceptions.AggregateException>(() => repository.Get("test"));
        }

        [Test]
        public async Task verify_version_no_dirty()
        {
            _stream.Setup(x => x.Dirty).Returns(false);
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            _stream.Setup(x => x.VerifyVersion(Moq.It.IsAny<Guid>())).Returns(Task.CompletedTask);

            var entity = await _repository.Get("test");
            await (_repository as IRepository).Prepare(Guid.NewGuid());

            _stream.Verify(x => x.VerifyVersion(Moq.It.IsAny<Guid>()), Moq.Times.Once);
        }

        [Test]
        public async Task verify_version_dirty()
        {
            _stream.Setup(x => x.Dirty).Returns(true);
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            _stream.Setup(x => x.VerifyVersion(Moq.It.IsAny<Guid>())).Returns(Task.CompletedTask);

            var entity = await _repository.Get("test");

            await (_repository as IRepository).Prepare(Guid.NewGuid());

            _stream.Verify(x => x.VerifyVersion(Moq.It.IsAny<Guid>()), Moq.Times.Never);
        }

        [Test]
        public async Task verify_version_throws_version_exception()
        {
            _stream.Setup(x => x.Dirty).Returns(false);
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));

            _stream.Setup(x => x.VerifyVersion(Moq.It.IsAny<Guid>())).Throws(new VersionException("test"));
            _streams.Setup(x => x.Evict<Entity>(Moq.It.IsAny<IEventStream>())).Returns(Task.CompletedTask);

            var entity = await _repository.Get("test");
            Assert.ThrowsAsync<VersionException>(() => (_repository as IRepository).Prepare(Guid.NewGuid()));

            _stream.Verify(x => x.VerifyVersion(Moq.It.IsAny<Guid>()), Moq.Times.Once);
            _streams.Verify(x => x.Evict<Entity>(Moq.It.IsAny<IEventStream>()), Moq.Times.Once);
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
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            var entity = await _repository.Get("test");

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());
            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
        }

        [Test]
        public async Task commit_take_snapshot_but_not_changed()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);
            _stream.Setup(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.CommitVersion).Returns(0);

            var entity = await _repository.Get("test");
            entity.TestTakeSnapshot = true;

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _stream.Verify(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()), Moq.Times.Never);
        }

        [Test]
        public async Task commit_take_snapshot()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), new Id("test"),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);
            _stream.Setup(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = await _repository.Get("test");
            entity.TestTakeSnapshot = true;

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            _stream.Verify(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()), Moq.Times.Once);

        }

        [Test]
        public async Task commit_version_exception_new_stream_doesnt_start_resolution()
        {
            _builder.Setup(x => x.Build<ThrowConflictResolver>());

            _streams.Setup(x => x.NewStream<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws(new VersionException("test"));
            _stream.Setup(x => x.CommitVersion).Returns(-1);

            var entity = await _repository.New("test");

            Assert.ThrowsAsync<ConflictResolutionFailedException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _builder.Verify(x => x.Build<ThrowConflictResolver>(), Moq.Times.Never);
        }

        [Test]
        public async Task commit_version_exception_starts_resolution()
        {
            _resolver.Fail = true;

            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws(new VersionException("test"));
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = await _repository.Get("test");

            Assert.ThrowsAsync<ConflictResolutionFailedException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _builder.Verify(x => x.Build(typeof(FakeResolver)), Moq.Times.Once);
        }

        [Test]
        public async Task commit_version_exception_resolution_succeeds()
        {

            _resolver.Fail = false;

            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws(new VersionException("test"));
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = await _repository.Get("test");

            Assert.DoesNotThrowAsync(
                () => (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _builder.Verify(x => x.Build(typeof(FakeResolver)), Moq.Times.Once);
        }
        [Test]
        public async Task commit_version_exception_resolution_throws_abandon()
        {
            _resolver.Abandon = true;

            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws(new VersionException("test"));
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = await _repository.Get("test");

            Assert.ThrowsAsync<ConflictResolutionFailedException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _builder.Verify(x => x.Build(typeof(FakeResolver)), Moq.Times.Once);
        }


        [Test]
        public async Task commit_persistence_exception()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws(new PersistenceException("test"));
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = await _repository.Get("test");

            Assert.ThrowsAsync<PersistenceException>(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

        }
        [Test]
        public async Task commit_duplicate_commit_exception()
        {
            _streams.Setup(x => x.GetStream<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<ISnapshot>())).Returns(Task.FromResult(_stream.Object));
            _stream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws(new DuplicateCommitException("test"));
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = await _repository.Get("test");

            Assert.DoesNotThrowAsync(() =>
                (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));

            _stream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
        }

    }
}
