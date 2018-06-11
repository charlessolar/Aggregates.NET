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
    public class DelayedChannel : TestSubject<Internal.DelayedChannel>
    {
        protected override Internal.DelayedChannel CreateSut()
        {
            var channel = base.CreateSut();
            channel.Begin().Wait();
            return channel;
        }

        [Fact]
        public async Task AddToQueue()
        {
            var cache = Fake<IDelayedCache>();
            A.CallTo(() => cache.Size("test", null)).Returns(0);
            Inject(cache);

            await Sut.AddToQueue("test", Fake<IDelayedMessage>()).ConfigureAwait(false);

            var size = await Sut.Size("test").ConfigureAwait(false);
            size.Should().Be(1);
        }

        [Fact]
        public async Task UowEndStoresInCache()
        {
            var cache = Fake<IDelayedCache>();
            Inject(cache);

            await Sut.AddToQueue("test", Fake<IDelayedMessage>()).ConfigureAwait(false);
            await Sut.End().ConfigureAwait(false);

            A.CallTo(() => cache.Add("test", null, A<IDelayedMessage[]>.Ignored)).Should().HaveHappened();
        }
        [Fact]
        public async Task UowEndExceptionShouldNotStoreInCache()
        {
            var cache = Fake<IDelayedCache>();
            Inject(cache);

            await Sut.AddToQueue("test", Fake<IDelayedMessage>()).ConfigureAwait(false);
            await Sut.End(new Exception()).ConfigureAwait(false);

            A.CallTo(() => cache.Add("test", null, A<IDelayedMessage[]>.Ignored)).Should().NotHaveHappened();
        }
        [Fact]
        public async Task UowEndAfterPull()
        {
            var cache = Fake<IDelayedCache>();
            A.CallTo(() => cache.Pull(A<string>.Ignored, A<string>.Ignored, A<int?>.Ignored)).Returns(Many<IDelayedMessage>());
            Inject(cache);

            await Sut.Pull("test").ConfigureAwait(false);
            await Sut.End().ConfigureAwait(false);

            A.CallTo(() => cache.Add("test", null, A<IDelayedMessage[]>.Ignored)).Should().NotHaveHappened();
        }
        [Fact]
        public async Task UowEndExceptionAfterPullPutsMessagesBack()
        {
            var cache = Fake<IDelayedCache>();
            A.CallTo(() => cache.Pull(A<string>.Ignored, A<string>.Ignored, A<int?>.Ignored)).Returns(Many<IDelayedMessage>());
            Inject(cache);

            await Sut.Pull("test").ConfigureAwait(false);
            await Sut.End(new Exception()).ConfigureAwait(false);

            A.CallTo(() => cache.Add("test", null, A<IDelayedMessage[]>.Ignored)).Should().HaveHappened();
        }
        [Fact]
        public async Task PullChannelWithUncomitted()
        {
            var cache = Fake<IDelayedCache>();
            A.CallTo(() => cache.Pull(A<string>.Ignored, A<string>.Ignored, A<int?>.Ignored)).Returns(Many<IDelayedMessage>());
            Inject(cache);

            await Sut.AddToQueue("test", Fake<IDelayedMessage>()).ConfigureAwait(false);
            var results = await Sut.Pull("test").ConfigureAwait(false);

            results.Should().HaveCount(4);
        }
    }
}
