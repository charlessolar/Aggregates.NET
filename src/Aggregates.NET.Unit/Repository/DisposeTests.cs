using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;

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
            _eventStream.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, String>>())).Verifiable();
            _eventStore.Setup(x => x.GetStream<_AggregateStub>(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32?>())).Returns(_eventStream.Object);
            _aggregate = new Moq.Mock<_AggregateStub>();
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IEventRouter>()).Returns(_eventRouter.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);

            _repository = new Aggregates.Internal.Repository<_AggregateStub>(_builder.Object);
        }

        [Test]
        public void dispose_no_streams()
        {
            Assert.DoesNotThrow(() => _repository.Dispose());
        }
    }
}