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

namespace Aggregates.Unit.EntityRepository
{
    public class BadEntity1 : Entity<Guid>
    {
        public BadEntity1() { }
    }

    public class BadEntity2 : Entity<Guid>
    {
        private BadEntity2(Int32 foo) { }
    }

    [TestFixture]
    public class FailTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IEventStream> _stream;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _stream = new Moq.Mock<IEventStream>();
        }

        [Test]
        public void entity_no_private_constructor()
        {
            var repo = new Aggregates.Internal.EntityRepository<BadEntity1>(_builder.Object, _stream.Object);
            Assert.Throws<AggregateException>(() => repo.New(Guid.NewGuid()));
        }
        [Test]
        public void entity_private_constructor_with_argument()
        {
            var repo = new Aggregates.Internal.EntityRepository<BadEntity2>(_builder.Object, _stream.Object);
            Assert.Throws<AggregateException>(() => repo.New(Guid.NewGuid()));
        }
    }
}
