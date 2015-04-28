using Aggregates.Contracts;
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
    public class CommitTests
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
            _eventStream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>())).Verifiable();
            _eventStream.Setup(x => x.ClearChanges()).Verifiable();
            _eventStore.Setup(x => x.GetStream<_AggregateStub>(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32?>())).Returns(_eventStream.Object);
            _aggregate = new Moq.Mock<_AggregateStub>();
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IEventRouter>()).Returns(_eventRouter.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_eventStore.Object);

            _repository = new Aggregates.Internal.Repository<_AggregateStub>(_builder.Object);
        }

        [Test]
        public void commit_no_streams()
        {
            Assert.DoesNotThrow(() => _repository.Commit(Guid.NewGuid(), null));
        }

        [Test]
        public void commit_with_stream()
        {
            var eventSource = _repository.New(Guid.NewGuid());

            Assert.DoesNotThrow(() => _repository.Commit(Guid.NewGuid(), null));
            _eventStream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), null), Moq.Times.Once);
        }

        [Test]
        public void commit_with_two_streams()
        {
            var eventSource1 = _repository.New(Guid.NewGuid());
            var eventSource2 = _repository.New(Guid.NewGuid());

            Assert.DoesNotThrow(() => _repository.Commit(Guid.NewGuid(), null));
            _eventStream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), null), Moq.Times.Exactly(2));
        }

        [Test]
        public void commit_with_headers()
        {
            var eventSource = _repository.New(Guid.NewGuid());

            Assert.DoesNotThrow(() => _repository.Commit(Guid.NewGuid(), new Dictionary<String, Object> { { "Test", "Test" } }));
            _eventStream.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>()), Moq.Times.Once);
        }
    }
}