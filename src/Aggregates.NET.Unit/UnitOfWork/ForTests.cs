using Aggregates.Contracts;
using Aggregates.Internal;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Repository
{
    [TestFixture]
    public class ForTests
    {
        private Moq.Mock<IContainer> _container;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private Moq.Mock<IRepository<IEventSource<Guid>>> _repository;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _container = new Moq.Mock<IContainer>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _repository = new Moq.Mock<IRepository<IEventSource<Guid>>>();
            _container.Setup(x => x.Build(typeof(IRepository<IEventSource<Guid>>))).Returns(_repository.Object);
            _container.Setup(x => x.BuildChildContainer()).Returns(_container.Object);

            _uow = new Aggregates.Internal.UnitOfWork(_container.Object, _eventStore.Object, _bus.Object);
        }

        [Test]
        public void Get_repository()
        {
            var repo = _uow.For<IEventSource<Guid>>();
            Assert.IsNotNull(repo);
        }

        [Test]
        public void Get_cached_repository()
        {
            var repo = _uow.For<IEventSource<Guid>>();
            var repo2 = _uow.For<IEventSource<Guid>>();
            Assert.AreEqual(repo, repo2);
        }


    }
}
