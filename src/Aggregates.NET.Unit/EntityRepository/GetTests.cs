using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.EntityRepository
{
    [TestFixture]
    public class GetTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IStoreSnapshots> _snaps;
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
            _snaps = new Moq.Mock<IStoreSnapshots>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _router = new Moq.Mock<IRouteResolver>();
            _stream.Setup(x => x.Events).Returns(new List<IWritableEvent>());

            _snaps.Setup(x => x.GetSnapshot<_EntityStub>(Moq.It.IsAny<String>(), Moq.It.IsAny<String>()));

            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(_router.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);
            _builder.Setup(x => x.Build<IStoreSnapshots>()).Returns(_snaps.Object);

            _repository = new Internal.EntityRepository<Guid, _EntityStub>(Guid.NewGuid(), _stream.Object, _builder.Object);
        }

        [Test]
        public void get_doesnt_exist()
        {
            Assert.Throws<NotFoundException>(() => _repository.Get(Guid.NewGuid()));
        }

        [Test]
        public void get_exists()
        {
            var id = Guid.NewGuid();
            _store.Setup(x => x.GetStream<_EntityStub>(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32?>())).Returns(_stream.Object);
            _stream.Setup(x => x.Events).Returns(new List<IWritableEvent>());
            var entity = _repository.Get(id);
            Assert.NotNull(entity);
        }

        [Test]
        public void get_invalid_stream()
        {
            var id = Guid.NewGuid();
            _stream.Setup(x => x.Events).Returns(new List<IWritableEvent>());

            Assert.Throws<NotFoundException>(() => _repository.Get(id));
        }
    }
}