using Aggregates.Contracts;
using EventStore.ClientAPI.Exceptions;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;

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
        private Moq.Mock<IQueryProcessor> _processor;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _guidRepository = new Moq.Mock<IRepository<_AggregateStub<Guid>>>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();
            _processor = new Moq.Mock<IQueryProcessor>();

            _builder.Setup(x => x.Build<IRepository<_AggregateStub<Guid>>>()).Returns(_guidRepository.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _repoFactory.Setup(x => x.ForAggregate<_AggregateStub<Guid>>(Moq.It.IsAny<IBuilder>())).Returns(_guidRepository.Object);
            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _repoFactory.Object, _processor.Object);
        }

        [Test]
        public void Commit_persistence_exception()
        {
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>())).Throws<PersistenceException>();
            var repo = _uow.For<_AggregateStub<Guid>>();
            Assert.Throws<PersistenceException>(() => _uow.Commit());
        }
    }
}