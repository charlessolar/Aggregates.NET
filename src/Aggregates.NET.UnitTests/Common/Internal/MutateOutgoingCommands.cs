using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class MutateOutgoingCommands
    {
        private class Command : ICommand { }

        private Aggregates.Internal.MutateOutgoingCommands _mutator;

        [SetUp]
        public void Setup()
        {
            var bus = new Moq.Mock<IBus>();
            _mutator = new Aggregates.Internal.MutateOutgoingCommands(bus.Object);
        }

        [Test]
        public void not_a_command()
        {
            var context = new Moq.Mock<IOutgoingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.OutgoingLogicalMessageInstance).Returns(new object());
            
            _mutator.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public void is_command_no_mutators()
        {
            var context = new Moq.Mock<IOutgoingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { });
            context.Setup(x => x.OutgoingLogicalMessageInstance).Returns(new Command());

            _mutator.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
            context.Verify(x => x.UpdateMessageInstance(Moq.It.IsAny<object>()), Moq.Times.Once);
        }
        [Test]
        public void is_command_with_mutators()
        {
            var context = new Moq.Mock<IOutgoingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var mutator = new Moq.Mock<ICommandMutator>();
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { mutator.Object });
            context.Setup(x => x.OutgoingLogicalMessageMessageType).Returns(typeof(Command));
            context.Setup(x => x.OutgoingLogicalMessageInstance).Returns(new Command());

            _mutator.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<ICommand>()), Moq.Times.Once);
            context.Verify(x => x.UpdateMessageInstance(Moq.It.IsAny<object>()), Moq.Times.Once);
        }
    }
}
