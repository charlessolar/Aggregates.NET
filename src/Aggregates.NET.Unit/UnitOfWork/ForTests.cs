using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;

namespace Aggregates.Unit.Repository
{
    [TestFixture]
    public class ForTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private Moq.Mock<IRepository<_AggregateStub>> _repository;
        private Moq.Mock<IRepositoryFactory> _repoFactory;
        private Moq.Mock<IQueryProcessor> _processor;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();
            _processor = new Moq.Mock<IQueryProcessor>();
            _bus = new Moq.Mock<IBus>();
            _repository = new Moq.Mock<IRepository<_AggregateStub>>();
            _builder.Setup(x => x.Build<IRepository<_AggregateStub>>()).Returns(_repository.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _repoFactory.Setup(x => x.ForAggregate<_AggregateStub>(Moq.It.IsAny<IBuilder>())).Returns(_repository.Object);

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _repoFactory.Object, _processor.Object);
        }

        [Test]
        public void Get_repository()
        {
            var repo = _uow.For<_AggregateStub>();
            Assert.IsNotNull(repo);
        }

        [Test]
        public void Get_cached_repository()
        {
            var repo = _uow.For<_AggregateStub>();
            var repo2 = _uow.For<_AggregateStub>();
            Assert.AreEqual(repo, repo2);
        }
    }
}