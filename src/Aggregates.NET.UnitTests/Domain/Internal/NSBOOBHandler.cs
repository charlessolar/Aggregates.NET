using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class NSBOOBHandler
    {
        class Entity : Aggregates.Aggregate<Entity> { }

        private Aggregates.Internal.NsbOobHandler _handler;

        private Moq.Mock<IFullEvent> _event;
        private Moq.Mock<IEndpointInstance> _instance;

        [SetUp]
        public void Setup()
        {
            _handler = new Aggregates.Internal.NsbOobHandler();
            _event = new Moq.Mock<IFullEvent>();
            _instance = new Moq.Mock<IEndpointInstance>();

            Bus.Instance = _instance.Object;
        }

        [Test]
        public void publish_to_bus()
        {
            object msg;
            PublishOptions options;
            _instance.Setup(x => x.Publish(Moq.It.IsAny<object>(), Moq.It.IsAny<PublishOptions>()))
                .Callback<object, PublishOptions>(
                    (m, o) =>
                    {
                        msg = m;
                        options = o;
                    });


        }

        [Test]
        public void retrieve_fails()
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Retrieve<Entity>("test", "test", new Id[] {}, 0, 1));
        }

        [Test]
        public async Task size_zero()
        {
            Assert.AreEqual(0L, await _handler.Size<Entity>("test", "test", new Id[] {}));
        }
    }
}
