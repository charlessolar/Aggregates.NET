using Aggregates.Contracts;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class DelayedChannel
    {
        private Moq.Mock<IDelayedCache> _cache;

        private Aggregates.Internal.DelayedChannel _channel;


        [SetUp]
        public void Setup()
        {
            _cache = new Moq.Mock<IDelayedCache>();

            _channel = new Internal.DelayedChannel(_cache.Object);

            _channel.Begin();
        }

        [Test]
        public async Task add_channel_not_cached_yet()
        {
            var msg = new Moq.Mock<IDelayedMessage>();
            await _channel.AddToQueue("test", msg.Object, "test");

            _cache.Verify(x => x.Add(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage[]>()), Moq.Times.Never);
        }
        [Test]
        public async Task add_channel_uow_end()
        {
            var msg = new Moq.Mock<IDelayedMessage>();
            await _channel.AddToQueue("test", msg.Object, "test");

            await _channel.End();

            _cache.Verify(x => x.Add(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage[]>()), Moq.Times.Once);
        }
        [Test]
        public async Task add_channel_uow_end_exception()
        {
            var msg = new Moq.Mock<IDelayedMessage>();
            await _channel.AddToQueue("test", msg.Object, "test");

            await _channel.End(new Exception());

            _cache.Verify(x => x.Add(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage[]>()), Moq.Times.Never);
        }
        [Test]
        public async Task pull_channel()
        {
            var msg = new Moq.Mock<IDelayedMessage>();
            _cache.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new[] { msg.Object }));

            var pulled = await _channel.Pull("test", "test");

            await _channel.End();

            _cache.Verify(x => x.Add(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage[]>()), Moq.Times.Never);
        }
        [Test]
        public async Task pull_channel_exception_returned()
        {
            var msg = new Moq.Mock<IDelayedMessage>();
            _cache.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new[] { msg.Object }));

            var pulled = await _channel.Pull("test", "test");

            await _channel.End(new Exception());

            _cache.Verify(x => x.Add(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage[]>()), Moq.Times.Once);
        }
        [Test]
        public async Task pull_channel_with_uncommitted()
        {
            var msg = new Moq.Mock<IDelayedMessage>();
            _cache.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new[] { msg.Object }));

            await _channel.AddToQueue("test", msg.Object, "test");

            var pulled = await _channel.Pull("test", "test");
            Assert.AreEqual(2, pulled.Count());

        }
    }
}
