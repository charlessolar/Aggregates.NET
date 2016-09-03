using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Settings;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class ExceptionRejector
    {
        private Aggregates.Internal.ExceptionRejector _rejector;

        [SetUp]
        public void Setup()
        {
            var bus = new Moq.Mock<IBus>();
            var settings = new Moq.Mock<ReadOnlySettings>();
            settings.Setup(x => x.Get<Int32>("MaxRetries")).Returns(2);

            _rejector = new Aggregates.Internal.ExceptionRejector(bus.Object, settings.Object);
        }

        [Test]
        public void no_problem()
        {
            var context = new Moq.Mock<IContextAccessor>();
            context.Setup(x => x.PhysicalMessageId).Returns("1");

            //Assert.DoesNotThrow(() => _rejector.Invoke(context.Object, Moq.It.IsAny<Action>()));

        }
    }
}
