using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.Unit.Repository
{
    [TestFixture]
    public class CommitFailTests
    {
        private Moq.Mock<IContainer> _container;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IEventStream> _eventStream;
        private Moq.Mock<IEventRouter> _eventRouter;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<Aggregate<Guid>> _aggregate;
        private IRepository<Aggregate<Guid>> _repository;

        [SetUp]
        public void Setup()
        {
            _container = new Moq.Mock<IContainer>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _eventStream = new Moq.Mock<IEventStream>();
            _eventRouter = new Moq.Mock<IEventRouter>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _eventStream.Setup(x => x.CommitChanges(Moq.It.IsAny<Guid>())).Verifiable();
            _eventStream.Setup(x => x.UncommittedHeaders).Returns(new Dictionary<String, Object>());
            _eventStream.Setup(x => x.ClearChanges()).Verifiable();
            _eventStore.Setup(x => x.CreateStream(Moq.It.IsAny<String>(), Moq.It.IsAny<String>())).Returns(_eventStream.Object);
            _aggregate = new Moq.Mock<Aggregate<Guid>>();
            _aggregate.Setup(x => x.Container).Returns(_container.Object);
            _container.Setup(x => x.BuildChildContainer()).Returns(_container.Object);
            _container.Setup(x => x.Build(typeof(IEventRouter))).Returns(_eventRouter.Object);
            _container.Setup(x => x.Build(typeof(IMessageCreator))).Returns(_eventFactory.Object);
            _container.Setup(x => x.Build(typeof(Aggregate<Guid>))).Returns(_aggregate.Object);

            _repository = new Aggregates.Internal.Repository<Aggregate<Guid>>(_container.Object, _eventStore.Object);
        }

        [Test]
        public void commit_throw_conflicting_command()
        {
            _eventStream.Setup(x => x.CommitChanges(Moq.It.IsAny<Guid>())).Throws(new ConcurrencyException());

            var eventSource = _repository.New(Guid.NewGuid()).Apply<CreateFake>(e => { });
            Assert.Throws<ConflictingCommandException>(() => _repository.Commit(Guid.NewGuid()));
        }

        [Test]
        public void commit_throw_conflicting_command_clear_stream()
        {
            _eventStream.Setup(x => x.CommitChanges(Moq.It.IsAny<Guid>())).Throws(new ConcurrencyException());

            var eventSource = _repository.New(Guid.NewGuid()).Apply<CreateFake>(e => { });
            Assert.Throws<ConflictingCommandException>(() => _repository.Commit(Guid.NewGuid()));
            _eventStream.Verify(x => x.ClearChanges(), Moq.Times.Once);
        }

        [Test]
        public void commit_throw_duplicate_commit_clear_stream()
        {
            _eventStream.Setup(x => x.CommitChanges(Moq.It.IsAny<Guid>())).Throws(new DuplicateCommitException());

            var eventSource = _repository.New(Guid.NewGuid()).Apply<CreateFake>(e => { });
            Assert.DoesNotThrow(() => _repository.Commit(Guid.NewGuid()));
            _eventStream.Verify(x => x.ClearChanges(), Moq.Times.Once);
        }
    }
}
