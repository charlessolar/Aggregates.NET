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

namespace Aggregates.Unit.Repository
{
    [TestFixture]
    public class DisposeTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IEventStream> _eventStream;
        private Moq.Mock<IEventRouter> _eventRouter;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<_AggregateStub> _aggregate;
        private IRepository<_AggregateStub> _repository;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _eventStream = new Moq.Mock<IEventStream>();
            _eventRouter = new Moq.Mock<IEventRouter>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _eventStream.Setup(x => x.CommitChanges(Moq.It.IsAny<Guid>())).Verifiable();
            _eventStream.Setup(x => x.UncommittedHeaders).Returns(new Dictionary<String, Object>());
            _eventStream.Setup(x => x.Dispose()).Verifiable();
            _eventStore.Setup(x => x.CreateStream(Moq.It.IsAny<String>(), Moq.It.IsAny<String>())).Returns(_eventStream.Object);
            _aggregate = new Moq.Mock<_AggregateStub>();
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IEventRouter>()).Returns(_eventRouter.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);

            _repository = new Aggregates.Internal.Repository<_AggregateStub>(_builder.Object, _eventStore.Object);
        }

        [Test]
        public void dispose_no_streams()
        {
            Assert.DoesNotThrow(() => _repository.Dispose());
        }

        [Test]
        public void dispose_stream_disposed()
        {
            var eventSource = _repository.New(Guid.NewGuid());

            Assert.DoesNotThrow(() => _repository.Dispose());
            _eventStream.Verify(x => x.Dispose(), Moq.Times.Once);
        }
    }
}
