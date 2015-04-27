using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;

namespace Aggregates.Unit.EntityRepository
{
    public class BadEntity1 : Entity<Guid>
    {
        public BadEntity1()
        {
        }
    }

    public class BadEntity2 : Entity<Guid>
    {
        private BadEntity2(Int32 foo)
        {
        }
    }

    [TestFixture]
    public class FailTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _stream;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _stream = new Moq.Mock<IEventStream>();

            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);
        }

        [Test]
        public void entity_no_private_constructor()
        {
            var repo = new Aggregates.Internal.EntityRepository<Guid, BadEntity1>(Guid.NewGuid(), _stream.Object, _builder.Object);
            Assert.Throws<AggregateException>(() => repo.New(Guid.NewGuid()));
        }

        [Test]
        public void entity_private_constructor_with_argument()
        {
            var repo = new Aggregates.Internal.EntityRepository<Guid, BadEntity2>(Guid.NewGuid(), _stream.Object, _builder.Object);
            Assert.Throws<AggregateException>(() => repo.New(Guid.NewGuid()));
        }
    }
}