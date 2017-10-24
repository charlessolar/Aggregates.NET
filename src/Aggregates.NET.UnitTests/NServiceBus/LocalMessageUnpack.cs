using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.UnitTests;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Pipeline;
using NUnit.Framework;

namespace Aggregates.NServiceBus
{
    [TestFixture]
    public class LocalMessageUnpack
    {

        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IIncomingLogicalMessageContext> _context;
        private Moq.Mock<Func<Task>> _next;

        private ContextBag _contextBag;
        private Aggregates.Internal.LocalMessageUnpack _executor;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _context = new Moq.Mock<IIncomingLogicalMessageContext>();
            _next = new Moq.Mock<Func<Task>>();
            _contextBag = new ContextBag();

            var fake = new FakeConfiguration();

            Configuration.Settings = fake;

            _metrics.Setup(x => x.Begin(Moq.It.IsAny<string>())).Returns(new Moq.Mock<ITimer>().Object);
            _context.Setup(x => x.Extensions).Returns(_contextBag);
            _context.Setup(x => x.MessageHeaders).Returns(new Dictionary<string, string>
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString()
            });
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new global::NServiceBus.Unicast.Messages.MessageMetadata(typeof(Messages.ICommand)), 1));

            _executor = new Internal.LocalMessageUnpack(_metrics.Object);
        }
        [Test]
        public async Task event_delivered()
        {
            _contextBag.Set(Defaults.LocalHeader, new object());

            await _executor.Invoke(_context.Object, _next.Object);

            _context.Verify(x => x.UpdateMessageInstance(Moq.It.IsAny<object>()), Moq.Times.Once);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        [Test]
        public async Task bulk_event_delivered()
        {
            var delayed = new Moq.Mock<IDelayedMessage>();

            delayed.Setup(x => x.Headers).Returns(new Dictionary<string, string>
            {
                ["test"] = "test"
            });

            var events = new IDelayedMessage[] { delayed.Object };

            _contextBag.Set(Defaults.LocalBulkHeader, events);

            var headers = new Dictionary<string, string>();
            _context.Setup(x => x.Headers).Returns(headers);

            await _executor.Invoke(_context.Object, _next.Object);
            
            _context.Verify(x => x.UpdateMessageInstance(Moq.It.IsAny<object>()), Moq.Times.Once);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        [Test]
        public async Task multiple_bulk_events()
        {
            var delayed = new Moq.Mock<IDelayedMessage>();
            delayed.Setup(x => x.Headers).Returns(new Dictionary<string, string>
            {
                ["test"] = "test"
            });

            var events = new IDelayedMessage[] { delayed.Object, delayed.Object, delayed.Object };

            _contextBag.Set(Defaults.LocalBulkHeader, events);

            var headers = new Dictionary<string, string>();
            _context.Setup(x => x.Headers).Returns(headers);

            await _executor.Invoke(_context.Object, _next.Object);
            
            _context.Verify(x => x.UpdateMessageInstance(Moq.It.IsAny<object>()), Moq.Times.Exactly(3));
            _next.Verify(x => x(), Moq.Times.Exactly(3));
        }
    }
}
