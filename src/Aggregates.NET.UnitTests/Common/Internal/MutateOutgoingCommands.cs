using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Pipeline;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class MutateOutgoingCommands
    {
        private class Command : ICommand
        {
            public Guid Id { get; set; }
        }

        private Aggregates.Internal.MutateOutgoingCommands _mutator;

        [SetUp]
        public void Setup()
        {
            _mutator = new Aggregates.Internal.MutateOutgoingCommands();
        }

        [Test]
        public async Task not_a_command()
        {
            var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var mutator = new Moq.Mock<ICommandMutator>();
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { mutator.Object });
            context.Setup(x => x.Message).Returns(new OutgoingLogicalMessage(typeof(object), new object()));

            await _mutator.Invoke(context.Object, next.Object);
            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>()), Moq.Times.Never);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public async Task is_command_no_mutators()
        {
            var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { });
            context.Setup(x => x.Message).Returns(new OutgoingLogicalMessage(typeof(Command), new Command()));

            await _mutator.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
            context.Verify(x => x.UpdateMessage(Moq.It.IsAny<Command>()), Moq.Times.Never);
        }
        [Test]
        public async Task is_command_with_mutators()
        {
            var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var mutator = new Moq.Mock<ICommandMutator>();
            var mutating = new Moq.Mock<IMutating>();
            mutating.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns(mutating.Object);
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { mutator.Object });
            context.Setup(x => x.Message).Returns(new OutgoingLogicalMessage(typeof(Command), new Command()));

            await _mutator.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>()), Moq.Times.Once);
            context.Verify(x => x.UpdateMessage(Moq.It.IsAny<object>()), Moq.Times.Once);
        }

        [Test]
        public async Task mutator_changes_headers()
        {
            var headers = new Dictionary<string, string>() { ["Test"] = "fail" };
            var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var mutator = new Moq.Mock<ICommandMutator>();
            var mutating = new Moq.Mock<IMutating>();
            mutating.Setup(x => x.Headers).Returns(new Dictionary<string, string>() { ["Test"] = "test" });
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns(mutating.Object);
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { mutator.Object });
            context.Setup(x => x.Message).Returns(new OutgoingLogicalMessage(typeof(Command), new Command()));
            context.Setup(x => x.Headers).Returns(headers);

            await _mutator.Invoke(context.Object, next.Object);
            Assert.AreEqual(headers["Test"], "test");
        }

        [Test]
        public async Task mutator_changes_instance()
        {
            var checkCmd = new Command { Id = Guid.NewGuid() };
            var context = new Moq.Mock<IOutgoingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var mutator = new Moq.Mock<ICommandMutator>();
            var mutating = new Moq.Mock<IMutating>();
            mutating.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            mutating.Setup(x => x.Message).Returns(checkCmd);
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns(mutating.Object);
            context.Setup(x => x.Builder.BuildAll<ICommandMutator>()).Returns(new ICommandMutator[] { mutator.Object });
            context.Setup(x => x.Message).Returns(new OutgoingLogicalMessage(typeof(Command), new Command {Id=Guid.NewGuid()}));

            await _mutator.Invoke(context.Object, next.Object);
            context.Verify(x => x.UpdateMessage(checkCmd), Moq.Times.Once);
        }
    }
}
