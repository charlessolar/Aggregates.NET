using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class DelayedCache : TestSubject<Internal.DelayedCache>
    {

        [Fact]
        public async Task CanAddAndCanPull()
        {
            await Sut.Add("test", "test", Many<IDelayedMessage>()).ConfigureAwait(false);

            var results = await Sut.Pull("test", "test").ConfigureAwait(false);

            results.Should().HaveCount(3);
        }
        [Fact]
        public async Task MaxPullLeavesSomeLeft()
        {
            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            var results = await Sut.Pull("test", "test", max: 2).ConfigureAwait(false);

            results.Should().HaveCount(2);
            var size = await Sut.Size("test", "test").ConfigureAwait(false);
            size.Should().Be(3);
        }
        [Fact]
        public async Task AddMultiplePullInCorrectOrder()
        {
            var message = Fake<IDelayedMessage>();
            A.CallTo(() => message.MessageId).Returns("first");

            await Sut.Add("test", "test", new[] { message }).ConfigureAwait(false);
            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            var results = await Sut.Pull("test", "test", max: 2).ConfigureAwait(false);

            results[0].MessageId.Should().Be("first");
        }
        [Fact]
        public async Task OccasionallyPullsEmptyKey()
        {
            var random = Fake<IRandomProvider>();
            A.CallTo(() => random.Chance(A<int>.Ignored)).Returns(true);
            Inject(random);

            await Sut.Add("test", "test", Many<IDelayedMessage>(1)).ConfigureAwait(false);
            await Sut.Add("test", null, Many<IDelayedMessage>(1)).ConfigureAwait(false);

            var results = await Sut.Pull("test", "test").ConfigureAwait(false);
            results.Should().HaveCount(2);
        }

        [Fact]
        public async Task AgeOfMessages()
        {
            var time = Fake<ITimeProvider>();
            A.CallTo(() => time.Now).Returns(DateTime.UtcNow.AddDays(-1));
            Inject(time);

            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            A.CallTo(() => time.Now).Returns(DateTime.UtcNow);

            var age = await Sut.Age("test", "test").ConfigureAwait(false);
            age.Should().BeGreaterOrEqualTo(TimeSpan.FromDays(1));
        }

        [Fact]
        public async Task FlushesExpiredMessages()
        {
            var time = Fake<ITimeProvider>();
            A.CallTo(() => time.Now).Returns(DateTime.UtcNow.AddMinutes(-1));
            Inject(time);

            Settings.SetDelayedExpiration(TimeSpan.FromSeconds(1)).SetFlushSize(5).SetFlushInterval(TimeSpan.FromSeconds(1));

            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            A.CallTo(() => time.Now).Returns(DateTime.UtcNow);
            await Task.Delay(2000).ConfigureAwait(false);

            var size = await Sut.Size("test", "test").ConfigureAwait(false);
            size.Should().Be(0);
        }
        [Fact]
        public async Task FlushingFailureKeepsMessagesInCache()
        {
            var time = Fake<ITimeProvider>();
            A.CallTo(() => time.Now).Returns(DateTime.UtcNow.AddMinutes(-1));
            Inject(time);


            Settings.SetDelayedExpiration(TimeSpan.FromSeconds(1)).SetFlushSize(5).SetFlushInterval(TimeSpan.FromSeconds(1));

            var store = Fake<IStoreEvents>();
            A.CallTo(() => store.WriteEvents(A<string>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored)).Throws(new Exception());
            Inject(store);

            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            A.CallTo(() => time.Now).Returns(DateTime.UtcNow);
            await Task.Delay(2000).ConfigureAwait(false);

            var size = await Sut.Size("test", "test").ConfigureAwait(false);
            size.Should().Be(5);
        }
        [Fact]
        public async Task CacheFlushesWhenGetsTooBig()
        {
            Settings.SetMaxDelayed(0).SetFlushSize(5).SetFlushInterval(TimeSpan.FromSeconds(1));

            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            await Task.Delay(2000).ConfigureAwait(false);

            var size = await Sut.Size("test", "test").ConfigureAwait(false);
            size.Should().Be(0);
        }
        [Fact]
        public async Task TooBigFlushFailureKeepsMessagesInCache()
        {
            Settings.SetMaxDelayed(0).SetFlushSize(5).SetFlushInterval(TimeSpan.FromSeconds(1));

            var store = Fake<IStoreEvents>();
            A.CallTo(() => store.WriteEvents(A<string>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored)).Throws(new Exception());
            Inject(store);

            await Sut.Add("test", "test", Many<IDelayedMessage>(5)).ConfigureAwait(false);

            await Task.Delay(2000).ConfigureAwait(false);

            var size = await Sut.Size("test", "test").ConfigureAwait(false);
            size.Should().Be(5);
        }

    }
}
