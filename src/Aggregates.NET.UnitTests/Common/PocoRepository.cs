using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Aggregates.Exceptions;
using Aggregates.Contracts;
using System.IO;
using Aggregates.Internal;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class PocoRepository
    {
        class Poco
        {
            public string Foo;
        }

        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IStorePocos> _store;
        private IMessageSerializer _serializer;
        private Moq.Mock<IDomainUnitOfWork> _uow;
        private Moq.Mock<IEventMapper> _mapper;
        private Aggregates.Internal.PocoRepository<Poco> _repository;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _store = new Moq.Mock<IStorePocos>();
            _mapper = new Moq.Mock<IEventMapper>();
            _uow = new Moq.Mock<IDomainUnitOfWork>();

            _serializer = new JsonMessageSerializer(_mapper.Object, null, null, null, null);

            var fake = new FakeConfiguration();

            Configuration.Build(fake).Wait();
            
            _repository = new Aggregates.Internal.PocoRepository<Poco>(_metrics.Object, _store.Object, _serializer, _uow.Object);
            
            _store.Setup(
                    x => x.Write<Poco>(Moq.It.IsAny<Tuple<long, Poco>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                            Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);
        }

        [Test]
        public async Task get_from_store()
        {
            _store.Setup(x => x.Get<Poco>(Defaults.Bucket, new Id("test"), null))
                .Returns(Task.FromResult(new Tuple<long, Poco>(0, new Poco())));

            var poco = await _repository.Get("test").ConfigureAwait(false);

            Assert.NotNull(poco);
            Assert.AreEqual(0, _repository.ChangedStreams);
        }

        [Test]
        public async Task new_poco()
        {
            var poco = await _repository.New("test").ConfigureAwait(false);

            Assert.NotNull(poco);
            Assert.AreEqual(1, _repository.ChangedStreams);
        }

        [Test]
        public async Task get_after_new()
        {
            var poco = await _repository.New("test").ConfigureAwait(false);
            poco.Foo = "test";

            var poco2 = await _repository.Get("test").ConfigureAwait(false);
            Assert.AreEqual("test", poco2.Foo);
        }

        [Test]
        public async Task new_twice_throws()
        {
            var poco = await _repository.New("test").ConfigureAwait(false);

            Assert.ThrowsAsync<InvalidOperationException>(() => _repository.New("test"));
        }

        [Test]
        public async Task get_with_bucket()
        {
            _store.Setup(x => x.Get<Poco>("test", new Id("test"), null))
                .Returns(Task.FromResult(new Tuple<long, Poco>(0, new Poco())));

            var poco = await _repository.Get("test", "test").ConfigureAwait(false);

            Assert.NotNull(poco);
        }

        [Test]
        public async Task new_with_bucket()
        {
            var poco = await _repository.New("test", "test").ConfigureAwait(false);
            Assert.NotNull(poco);
        }

        [Test]
        public async Task bucket_is_used()
        {
            var poco = await _repository.New("test", "test").ConfigureAwait(false);

            var poco2 = await _repository.TryGet("test").ConfigureAwait(false);

            Assert.Null(poco2);
        }

        [Test]
        public void throw_not_found()
        {
            Assert.ThrowsAsync<NotFoundException>(() => _repository.Get("test"));
            Assert.ThrowsAsync<NotFoundException>(() => _repository.Get("test", "test"));

            Assert.AreEqual(0, _repository.ChangedStreams);
        }

        [Test]
        public async Task new_counts_as_change()
        {
            var poco = await _repository.New("test").ConfigureAwait(false);
            Assert.AreEqual(1, _repository.ChangedStreams);
        }

        [Test]
        public async Task get_unchanged_then_changed()
        {
            _store.Setup(x => x.Get<Poco>(Moq.It.IsAny<string>(), new Id("test"), Moq.It.IsAny<Id[]>()))
                .Returns(Task.FromResult(new Tuple<long, Poco>(0, new Poco())));

            var poco = await _repository.Get("test").ConfigureAwait(false);

            Assert.AreEqual(0, _repository.ChangedStreams);

            poco.Foo = "test";

            Assert.AreEqual(1, _repository.ChangedStreams);
        }

        [Test]
        public async Task commit_empty()
        {

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x =>
                    x.Write<Poco>(Moq.It.IsAny<Tuple<long, Poco>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);

        }
        [Test]
        public async Task commit_no_changes()
        {

            _store.Setup(x => x.Get<Poco>(Defaults.Bucket, new Id("test"), null))
                .Returns(Task.FromResult(new Tuple<long, Poco>(0, new Poco())));

            var poco = await _repository.Get("test").ConfigureAwait(false);
            
            Assert.AreEqual(0, _repository.ChangedStreams);

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x =>
                    x.Write<Poco>(Moq.It.IsAny<Tuple<long, Poco>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);

        }


        [Test]
        public async Task commit_with_changes()
        {

            _store.Setup(x => x.Get<Poco>(Defaults.Bucket, new Id("test"), null))
                .Returns(Task.FromResult(new Tuple<long, Poco>(0, new Poco())));

            var poco = await _repository.Get("test").ConfigureAwait(false);
            poco.Foo = "test";
            
            Assert.AreEqual(1, _repository.ChangedStreams);

            await (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x =>
                    x.Write<Poco>(Moq.It.IsAny<Tuple<long, Poco>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);

        }

        [Test]
        public void persistence_exception()
        {
            _store.Setup(
                    x =>
                        x.Write<Poco>(Moq.It.IsAny<Tuple<long, Poco>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                            Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws<PersistenceException>();

            var poco = _repository.New("test").ConfigureAwait(false);

            Assert.ThrowsAsync<PersistenceException>(() =>
                    (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));
        }


        [Test]
        public void throws_any_other_exception()
        {
            _store.Setup(
                    x =>
                        x.Write<Poco>(Moq.It.IsAny<Tuple<long, Poco>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                            Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Throws<Exception>();

            var poco = _repository.New("test").ConfigureAwait(false);

            Assert.ThrowsAsync<Exception>(() =>
                    (_repository as IRepository).Commit(Guid.NewGuid(), new Dictionary<string, string>()));
        }
    }
}
