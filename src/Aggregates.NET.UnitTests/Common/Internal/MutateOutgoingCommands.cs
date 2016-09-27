using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.Pipeline;
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
    //[TestFixture]
    //public class MutateOutgoingCommands
    //{
    //    private class Command : ICommand { }

    //    private Aggregates.Internal.MutateOutgoingCommands _mutator;

    //    [SetUp]
    //    public void Setup()
    //    {
    //        _mutator = new Aggregates.Internal.MutateOutgoingCommands();
    //    }

    //    [Test]
    //    public async Task not_a_command()
    //    {
    //        var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
    //        var next = new Moq.Mock<Func<Task>>();
    //        context.Setup(x => x.Message.Instance).Returns(new object());
            
    //        await _mutator.Invoke(context.Object, next.Object);
    //        next.Verify(x => x(), Moq.Times.Once);
    //    }
    //    [Test]
    //    public async Task is_command_no_mutators()
    //    {
    //        var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
    //        var next = new Moq.Mock<Func<Task>>();
    //        context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { });
    //        context.Setup(x => x.Message.Instance).Returns(new Command());

    //        await _mutator.Invoke(context.Object, next.Object);
    //        next.Verify(x => x(), Moq.Times.Once);
    //        context.Verify(x => x.UpdateMessage(Moq.It.IsAny<object>()), Moq.Times.Once);
    //    }
    //    [Test]
    //    public async Task is_command_with_mutators()
    //    {
    //        var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
    //        var next = new Moq.Mock<Func<Task>>();
    //        var mutator = new Moq.Mock<ICommandMutator>();
    //        context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { mutator.Object });
    //        context.Setup(x => x.Message.MessageType).Returns(typeof(Command));
    //        context.Setup(x => x.Message.Instance).Returns(new Command());

    //        await _mutator.Invoke(context.Object, next.Object);
    //        next.Verify(x => x(), Moq.Times.Once);
    //        mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<ICommand>()), Moq.Times.Once);
    //        context.Verify(x => x.UpdateMessage(Moq.It.IsAny<object>()), Moq.Times.Once);
    //    }
    //}
}
