using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class DisposeTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IRepositoryFactory> _repoFactory;
        private Moq.Mock<IQueryProcessor> _processor;
        private Moq.Mock<IRepository<_AggregateStub<Guid>>> _repository;
        private Moq.Mock<IBus> _bus;
        private Aggregates.Internal.UnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();
            _processor = new Moq.Mock<IQueryProcessor>();
            _bus = new Moq.Mock<IBus>();
            _repository = new Moq.Mock<IRepository<_AggregateStub<Guid>>>();
            _repository.Setup(x => x.Dispose()).Verifiable();
            _repoFactory.Setup(x => x.ForAggregate<_AggregateStub<Guid>>(Moq.It.IsAny<IBuilder>())).Returns(_repository.Object);

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _repoFactory.Object, _processor.Object);
        }

        [Test]
        public void Dispose_repository_is_disposed()
        {
            var repo = _uow.For<_AggregateStub<Guid>>();
            _uow.Dispose();
            _repository.Verify(x => x.Dispose(), Moq.Times.Once);
        }
    }
}