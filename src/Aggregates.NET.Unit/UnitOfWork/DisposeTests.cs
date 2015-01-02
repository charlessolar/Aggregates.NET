using Aggregates.Contracts;
using NEventStore;
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
    public class DisposeTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IRepositoryFactory> _repoFactory;
        private Moq.Mock<IRepository<IEventSource<Guid>>> _repository;
        private Moq.Mock<IBus> _bus;
        private Aggregates.Internal.UnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();
            _bus = new Moq.Mock<IBus>();
            _repository = new Moq.Mock<IRepository<IEventSource<Guid>>>();
            _repository.Setup(x => x.Dispose()).Verifiable();
            _repoFactory.Setup(x => x.Create<IEventSource<Guid>>(Moq.It.IsAny<IBuilder>(), Moq.It.IsAny<IStoreEvents>())).Returns(_repository.Object);

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _eventStore.Object, _repoFactory.Object);
        }

        [Test]
        public void Dispose_repository_is_disposed()
        {
            var repo = _uow.For<IEventSource<Guid>>();
            _uow.Dispose();
            _repository.Verify(x => x.Dispose(), Moq.Times.Once);
        }
    }
}
