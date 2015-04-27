using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Aggregates.Unit.EntityRepository
{
    [TestFixture]
    public class NewTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<IRouteResolver> _router;
        private Aggregates.Internal.EntityRepository<Guid, _EntityStub> _repository;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _stream = new Moq.Mock<IEventStream>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _router = new Moq.Mock<IRouteResolver>();
            _stream.Setup(x => x.Events).Returns(new List<IWritableEvent>());
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(_router.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);

            _repository = new Internal.EntityRepository<Guid, _EntityStub>(Guid.NewGuid(), _stream.Object, _builder.Object);
        }

        [Test]
        public void new_not_null()
        {
            var entity = _repository.New(Guid.NewGuid());
            Assert.NotNull(entity);
        }
    }
}