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

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class DisposeTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _eventStore.Setup(x => x.Dispose()).Verifiable();
        }

        [Test]
        public void Dispose_eventstore_is_disposed()
        {
            var uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _eventStore.Object);
            uow.Dispose();
            _eventStore.Verify(x => x.Dispose(), Moq.Times.Once);
        }
    }
}
