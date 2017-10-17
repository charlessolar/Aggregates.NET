using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Pipeline;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.UnitTests.NServiceBus
{
    [TestFixture]
    public class UnitOfWorkExecutor
    {

        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IIncomingLogicalMessageContext> _context;
        private Moq.Mock<IDomainUnitOfWork> _domainUow;
        private Moq.Mock<IUnitOfWork> _uow;
        private Moq.Mock<IDelayedChannel> _channel;
        private Moq.Mock<Func<Task>> _next;

        private ContextBag _contextBag;
        private Aggregates.Internal.UnitOfWorkExecutor _executor;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _context = new Moq.Mock<IIncomingLogicalMessageContext>();
            _domainUow = new Moq.Mock<IDomainUnitOfWork>();
            _uow = new Moq.Mock<IUnitOfWork>();
            _channel = new Moq.Mock<IDelayedChannel>();
            _next = new Moq.Mock<Func<Task>>();
            _contextBag = new ContextBag();

            var fake = new FakeConfiguration();
            fake.FakeContainer.Setup(x => x.Resolve<IDomainUnitOfWork>()).Returns(_domainUow.Object);
            fake.FakeContainer.Setup(x => x.Resolve<IUnitOfWork>()).Returns(_uow.Object);
            fake.FakeContainer.Setup(x => x.Resolve<IDelayedChannel>()).Returns(_channel.Object);

            Configuration.Settings = fake;
            
            _metrics.Setup(x => x.Begin(Moq.It.IsAny<string>())).Returns(new Moq.Mock<ITimer>().Object);
            _context.Setup(x => x.Extensions).Returns(_contextBag);
            _context.Setup(x => x.MessageHeaders).Returns(new Dictionary<string, string>
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString()
            });
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new global::NServiceBus.Unicast.Messages.MessageMetadata(typeof(Messages.ICommand)), 1));

            _executor = new Internal.UnitOfWorkExecutor(_metrics.Object);
        }

        [Test]
        public async Task normal()
        {
            await _executor.Invoke(_context.Object, _next.Object);

            _domainUow.Verify(x => x.Begin(), Moq.Times.Once);
            _uow.Verify(x => x.Begin(), Moq.Times.Once);
            _domainUow.Verify(x => x.End(Moq.It.IsAny<Exception>()), Moq.Times.Once);
            _uow.Verify(x => x.End(Moq.It.IsAny<Exception>()), Moq.Times.Once);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        [Test]
        public void throw_exception()
        {
            _next.Setup(x => x()).Throws(new Exception());

            Assert.ThrowsAsync<Exception>(() => _executor.Invoke(_context.Object, _next.Object));

            _domainUow.Verify(x => x.Begin(), Moq.Times.Once);
            _uow.Verify(x => x.Begin(), Moq.Times.Once);

            _domainUow.Verify(x => x.End(Moq.It.IsNotNull<Exception>()), Moq.Times.Once);
            _uow.Verify(x => x.End(Moq.It.IsNotNull<Exception>()), Moq.Times.Once);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        [Test]
        public void uow_end_throws_too()
        {
            _next.Setup(x => x()).Throws<Exception>();
            _uow.Setup(x => x.End(Moq.It.IsAny<Exception>())).Throws<Exception>();

            // Should produce AggregateException not Exception
            Assert.ThrowsAsync<AggregateException>(() => _executor.Invoke(_context.Object, _next.Object));

            _domainUow.Verify(x => x.Begin(), Moq.Times.Once);
            _uow.Verify(x => x.Begin(), Moq.Times.Once);

            _domainUow.Verify(x => x.End(Moq.It.IsNotNull<Exception>()), Moq.Times.Once);
            _uow.Verify(x => x.End(Moq.It.IsNotNull<Exception>()), Moq.Times.Once);
            _next.Verify(x => x(), Moq.Times.Once);
        }

        
    }
}
