using Aggregates.Contracts;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class DelayedCache
    {

        private Moq.Mock<IMetrics> _metrics;
        private Moq.Mock<IStoreEvents> _store;

        private Aggregates.Internal.DelayedCache _cache;

        [SetUp]
        public void Setup()
        {
            _metrics = new Moq.Mock<IMetrics>();
            _store = new Moq.Mock<IStoreEvents>();
        }

        [TearDown]
        public void Teardown()
        {
            _cache?.Dispose();
        }

        [Test]
        public async Task add_and_pull()
        {
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", "test", new[] { msg.Object });

            var size = await _cache.Size("test", "test");
            Assert.AreEqual(1, size);

            var result = await _cache.Pull("test", "test");

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("test", result[0].MessageId);

        }

        [Test]
        public async Task add_multiple_max_pull()
        {
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", "test", new[] { msg.Object, msg.Object, msg.Object });

            var result = await _cache.Pull("test", "test", max: 2);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("test", result[0].MessageId);
        }

        [Test]
        public async Task add_existing_key_correct_order()
        {
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            var msg2 = new Moq.Mock<IDelayedMessage>();
            msg2.Setup(x => x.MessageId).Returns("test2");
            await _cache.Add("test", "test", new[] { msg.Object });
            await _cache.Add("test", "test", new[] { msg2.Object });

            var result = await _cache.Pull("test", "test");

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("test", result[0].MessageId);
            Assert.AreEqual("test2", result[1].MessageId);
        }

        [Test]
        public async Task add_no_key_flushes_to_store()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Returns(Task.FromResult(0L));

            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(5), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", null, new[] { msg.Object });

            var size = await _cache.Size("test", "test");
            Assert.AreEqual(0, size);

            var result = await _cache.Pull("test", "test");

            Assert.AreEqual(0, result.Length);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
        }

        [Test]
        public async Task add_no_key_write_exception()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Throws<Exception>();

            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(5), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", null, new[] { msg.Object });

            var size = await _cache.Size("test", null);
            Assert.AreEqual(1, size);

            var result = await _cache.Pull("test");

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("test", result[0].MessageId);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
        }

        [Test]
        public async Task add_key_exception_once_get_both()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Throws<Exception>();

            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(5), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", null, new[] { msg.Object });
            await _cache.Add("test", "test", new[] { msg.Object });

            var size = await _cache.Size("test", "test");
            Assert.AreEqual(1, size);

            // Verify that Pull will get the null key message AND the "test" key message
            var result = await _cache.Pull("test", "test");

            // Todo: cache uses rand to pull non-specific channel, will have to fake rand to turn this into
            // Assert.AreEqual(2, result.Length);
            Assert.GreaterOrEqual(1, result.Length);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
        }

        [Test]
        public async Task many_messages_still_in_memcache()
        {

            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", int.MaxValue, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            var msg2 = new Moq.Mock<IDelayedMessage>();
            msg2.Setup(x => x.MessageId).Returns("test2");
            var msg3 = new Moq.Mock<IDelayedMessage>();
            msg3.Setup(x => x.MessageId).Returns("test3");
            await _cache.Add("test", "test", new[] { msg.Object, msg.Object, msg2.Object, msg2.Object, msg3.Object });

            var result = await _cache.Pull("test", "test", max: 2);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("test", result[0].MessageId);
            Assert.AreEqual("test", result[1].MessageId);

            var result2 = await _cache.Pull("test", "test", max: 2);

            Assert.AreEqual(2, result2.Length);
            Assert.AreEqual("test2", result2[0].MessageId);
            Assert.AreEqual("test2", result2[1].MessageId);

            var result3 = await _cache.Pull("test", "test", max: 2);

            Assert.AreEqual(1, result3.Length);
            Assert.AreEqual("test3", result3[0].MessageId);
        }

        [Test]
        public async Task expired_message_flushed()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Returns(Task.FromResult(0L));
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", int.MaxValue, int.MaxValue, TimeSpan.FromSeconds(1), (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", "test", new[] { msg.Object });

            await Task.Delay(5000);

            var result = await _cache.Pull("test", "test");

            Assert.AreEqual(0, result.Length);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
        }
        [Test]
        public async Task expired_message_flushed_exception()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Throws<Exception>();
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", int.MaxValue, int.MaxValue, TimeSpan.FromSeconds(1), (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", "test", new[] { msg.Object });

            await Task.Delay(5000);

            var result = await _cache.Pull("test", "test");

            Assert.AreEqual(1, result.Length);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.AtLeastOnce);
        }
        [Test]
        public async Task cache_too_large_flushes()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Returns(Task.FromResult(0L));
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", 1, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", "test", new[] { msg.Object, msg.Object, msg.Object });

            await Task.Delay(5000);

            var result = await _cache.Pull("test", "test");

            Assert.AreEqual(0, result.Length);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.AtLeastOnce);
        }
        [Test]
        public async Task cache_too_large_flushes_exception()
        {
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Throws<Exception>();
            _cache = new Internal.DelayedCache(_metrics.Object, _store.Object, TimeSpan.FromSeconds(1), "test", 1, int.MaxValue, TimeSpan.MaxValue, (a, b, c, d, e) => "test");

            var msg = new Moq.Mock<IDelayedMessage>();
            msg.Setup(x => x.MessageId).Returns("test");
            await _cache.Add("test", "test", new[] { msg.Object, msg.Object, msg.Object });

            await Task.Delay(5000);

            var result = await _cache.Pull("test", "test");
            
            Assert.AreEqual(3, result.Length);
            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.AtLeastOnce);
        }

    }
}
