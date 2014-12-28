using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
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
    public class CommitTests
    {
        private Moq.Mock<IContainer> _container;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private Moq.Mock<IRepository<IEventSource<Guid>>> _guidRepository;
        private Moq.Mock<IRepository<IEventSource<Int32>>> _intRepository;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _container = new Moq.Mock<IContainer>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _guidRepository = new Moq.Mock<IRepository<IEventSource<Guid>>>();
            _intRepository = new Moq.Mock<IRepository<IEventSource<Int32>>>();
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Verifiable();
            _intRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Verifiable();
            _container.Setup(x => x.Build(typeof(IRepository<IEventSource<Guid>>))).Returns(_guidRepository.Object);
            _container.Setup(x => x.Build(typeof(IRepository<IEventSource<Int32>>))).Returns(_intRepository.Object);
            _container.Setup(x => x.BuildChildContainer()).Returns(_container.Object);
            _uow = new Aggregates.Internal.UnitOfWork(_container.Object, _eventStore.Object, _bus.Object);
        }

        [Test]
        public void Commit_no_events()
        {
            Assert.DoesNotThrow(() => _uow.Commit());
        }

        [Test]
        public void Commit_one_repo()
        {
            var repo = _uow.For<IEventSource<Guid>>();
            Assert.DoesNotThrow(() => _uow.Commit());
            _guidRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>()), Moq.Times.Once);
        }

        [Test]
        public void Commit_multiple_repo()
        {
            var repo = _uow.For<IEventSource<Guid>>();
            var repo2 = _uow.For<IEventSource<Int32>>();
            Assert.DoesNotThrow(() => _uow.Commit());
            _guidRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>()), Moq.Times.Once);
            _intRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>()), Moq.Times.Once);
        }

    }
}
