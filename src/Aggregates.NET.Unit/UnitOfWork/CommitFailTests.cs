using Aggregates.Contracts;
using NEventStore;
using NEventStore.Persistence;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class CommitFailTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private Moq.Mock<IRepository<_AggregateStub<Guid>>> _guidRepository;
        private Moq.Mock<IRepositoryFactory> _repoFactory;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _guidRepository = new Moq.Mock<IRepository<_AggregateStub<Guid>>>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();

            _builder.Setup(x => x.Build<IRepository<_AggregateStub<Guid>>>()).Returns(_guidRepository.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _repoFactory.Setup(x => x.ForAggregate<_AggregateStub<Guid>>(Moq.It.IsAny<IBuilder>(), Moq.It.IsAny<IStoreEvents>())).Returns(_guidRepository.Object);
            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _eventStore.Object, _repoFactory.Object);
        }

        [Test]
        public void Commit_persistence_exception()
        {
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Throws<StorageException>();
            var repo = _uow.For<_AggregateStub<Guid>>();
            Assert.Throws<PersistenceException>(() => _uow.Commit());
        }

        [Test]
        public void Commit_persistence_exception_unavailable()
        {
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Throws<StorageUnavailableException>();
            var repo = _uow.For<_AggregateStub<Guid>>();
            Assert.Throws<PersistenceException>(() => _uow.Commit());
        }
    }
}
