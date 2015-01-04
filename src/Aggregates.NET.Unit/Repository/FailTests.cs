using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Repository
{
    public class BadAggregate1 : Aggregate<Guid>
    {
        public BadAggregate1() { }
    }

    public class BadAggregate2 : Aggregate<Guid>
    {
        private BadAggregate2(Int32 foo) { }
    }

    [TestFixture]
    public class FailTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
        }

        [Test]
        public void entity_no_private_constructor()
        {
            var repo = new Aggregates.Internal.Repository<BadAggregate1>(_builder.Object, _store.Object);
            Assert.Throws<AggregateException>(() => repo.New(Guid.NewGuid()));
        }
        [Test]
        public void entity_private_constructor_with_argument()
        {
            var repo = new Aggregates.Internal.Repository<BadAggregate2>(_builder.Object, _store.Object);
            Assert.Throws<AggregateException>(() => repo.New(Guid.NewGuid()));
        }
    }
}
