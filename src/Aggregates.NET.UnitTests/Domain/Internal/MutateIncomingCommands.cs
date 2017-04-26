using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class MutateIncomingCommands
    {
        class FakeCommand : ICommand { }

        private Aggregates.Internal.MutateIncomingCommands _pipeline;

        private ContextBag _bag;
        private Moq.Mock<IIncomingLogicalMessageContext> _context;
        private Moq.Mock<Func<Task>> _next;
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<ICommandMutator> _mutator;
        private Dictionary<string, string> _headers;

        [SetUp]
        public void Setup()
        {
            _pipeline = new Aggregates.Internal.MutateIncomingCommands();
            _headers = new Dictionary<string, string>();

            _bag = new ContextBag();
            _context = new Moq.Mock<IIncomingLogicalMessageContext>();
            _next = new Moq.Mock<Func<Task>>();
            _builder = new Moq.Mock<IBuilder>();
            _mutator = new Moq.Mock<ICommandMutator>();

            _context.Setup(x => x.Headers).Returns(_headers);
            _context.Setup(x => x.MessageId).Returns("1");
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(object)), new object()));
            _context.Setup(x => x.Extensions).Returns(_bag);
            _context.Setup(x => x.Builder).Returns(_builder.Object);
            _context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string> { [Headers.MessageIntent] = MessageIntentEnum.Send.ToString() });
        }

        [Test]
        public async Task no_problem_no_mutators_not_command()
        {
            await _pipeline.Invoke(_context.Object, _next.Object);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        [Test]
        public async Task no_problem_no_mutators()
        {
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));

            await _pipeline.Invoke(_context.Object, _next.Object);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        [Test]
        public async Task no_problem_with_mutators()
        {
            _mutator.Setup(x => x.MutateIncoming(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x => x);
            _builder.Setup(x => x.BuildAll<ICommandMutator>()).Returns(new[] { _mutator.Object });
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));

            await _pipeline.Invoke(_context.Object, _next.Object);
            _mutator.Verify(x => x.MutateIncoming(Moq.It.IsAny<IMutating>()), Moq.Times.Once);
        }

        [Test]
        public async Task mutated_set_header()
        {
            _mutator.Setup(x => x.MutateIncoming(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x =>
            {
                x.Headers["test"] = "test";
                return x;
            });
            _builder.Setup(x => x.BuildAll<ICommandMutator>()).Returns(new[] { _mutator.Object });
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));

            await _pipeline.Invoke(_context.Object, _next.Object);
            _mutator.Verify(x => x.MutateIncoming(Moq.It.IsAny<IMutating>()), Moq.Times.Once);
            Assert.True(_headers.ContainsKey("test"));
            Assert.AreEqual("test", _headers["test"]);
        }

        [Test]
        public async Task mutated_changes_message()
        {
            _mutator.Setup(x => x.MutateIncoming(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x =>
            {
                x.Message = 1;
                return x;
            });
            _builder.Setup(x => x.BuildAll<ICommandMutator>()).Returns(new[] { _mutator.Object });
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            _context.Setup(x => x.UpdateMessageInstance(1));

            await _pipeline.Invoke(_context.Object, _next.Object);
            _context.Verify(x => x.UpdateMessageInstance(1), Moq.Times.Once);
        }
    }
}
