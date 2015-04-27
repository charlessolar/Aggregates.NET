using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Aggregates.Unit.Repository
{
    [TestFixture]
    public class GetTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _eventStream;
        private Moq.Mock<IEventRouter> _eventRouter;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<_AggregateStub> _aggregate;
        private Moq.Mock<IRouteResolver> _resolver;
        private IRepository<_AggregateStub> _repository;
        private Guid _id;

        [SetUp]
        public void Setup()
        {
            _id = Guid.NewGuid();
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _eventStream = new Moq.Mock<IEventStream>();
            _eventRouter = new Moq.Mock<IEventRouter>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _resolver = new Moq.Mock<IRouteResolver>();

            _eventStream.Setup(x => x.Events).Returns(new List<Object>());
            _store.Setup(x => x.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns((ISnapshot)null);
            _store.Setup(x => x.GetStream(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns(_eventStream.Object);
            _aggregate = new Moq.Mock<_AggregateStub>();
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<_AggregateStub>(), typeof(String))).Returns(e => { });
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IEventRouter>()).Returns(_eventRouter.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(_resolver.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);

            _repository = new Aggregates.Internal.Repository<_AggregateStub>(_builder.Object);
        }

        [Test]
        public void get_existing_no_events()
        {
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
        }

        [Test]
        public void get_non_existing()
        {
            _eventStream.Setup(x => x.StreamVersion).Returns(-1);
            Assert.IsNull(_repository.Get(Guid.NewGuid()));
        }

        [Test]
        public void get_existing_with_events()
        {
            _eventStream.Setup(x => x.Events).Returns(new List<Object> { "Test" });
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id));
        }

        [Test]
        public void get_existing_with_snapshot()
        {
            var snapshot = new Moq.Mock<ISnapshot>();
            _store.Setup(x => x.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns(snapshot.Object);
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id));
        }

        [Test]
        public void get_specific_version()
        {
            _eventStream.Setup(x => x.Events).Returns(new List<Object> { "Test", "Test", "Test" });
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id, 2));
        }

        [Test]
        public void get_cached_stream()
        {
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id));
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id));
        }

        [Test]
        public void get_cached_snapshot()
        {
            var snapshot = new Moq.Mock<ISnapshot>();
            _store.Setup(x => x.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns(snapshot.Object);
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id));
            Assert.IsInstanceOf<_AggregateStub>(_repository.Get(_id));
        }
    }
}