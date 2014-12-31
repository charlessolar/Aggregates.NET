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
        private Moq.Mock<IRepository<IEventSource<Guid>>> _guidRepository;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _guidRepository = new Moq.Mock<IRepository<IEventSource<Guid>>>();
            _builder.Setup(x => x.Build<IRepository<IEventSource<Guid>>>()).Returns(_guidRepository.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _eventStore.Object);
        }

        [Test]
        public void Commit_persistence_exception()
        {
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Throws<StorageException>();
            var repo = _uow.For<IEventSource<Guid>>();
            Assert.Throws<PersistenceException>(() => _uow.Commit());
        }

        [Test]
        public void Commit_persistence_exception_unavailable()
        {
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Throws<StorageUnavailableException>();
            var repo = _uow.For<IEventSource<Guid>>();
            Assert.Throws<PersistenceException>(() => _uow.Commit());
        }
    }
}
