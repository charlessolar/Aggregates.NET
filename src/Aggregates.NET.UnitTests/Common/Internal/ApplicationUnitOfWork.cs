using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus.Extensibility;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class ApplicationUnitOfWork
    {
        private Moq.Mock<IPersistence> _persistence;
        private Aggregates.Internal.ApplicationUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _persistence = new Moq.Mock<IPersistence>();
            _uow = new Aggregates.Internal.ApplicationUnitOfWork(_persistence.Object);
        }

        [Test]
        public async Task no_problem_no_units_of_work()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);

            await _uow.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public async Task no_problem_one_units_of_work()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            var uow = new Moq.Mock<IApplicationUnitOfWork>();
            builder.Setup(x => x.BuildAll<IApplicationUnitOfWork>()).Returns(new IApplicationUnitOfWork[] {uow.Object});
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);

            await _uow.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
    }
}
