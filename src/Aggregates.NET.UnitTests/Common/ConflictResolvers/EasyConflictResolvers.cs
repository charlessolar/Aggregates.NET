using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Messages;
using Aggregates.Internal;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Xunit;
using FakeItEasy;
using FluentAssertions;
using AutoFixture.Xunit2;

namespace Aggregates.UnitTests.Common.ConflictResolvers
{
    public class EasyConflictResolvers : Test
    {
        [Fact]
        async Task ThrowConflictResolverThrowsOnConflict()
        {
            var sut = new ThrowConflictResolver();

            var e = await Record.ExceptionAsync(() => 
                sut.Resolve<Fakes, FakeState>(Fake<Fakes>(), Fake<IFullEvent[]>(), Fake<Guid>(), Fake<Dictionary<string, string>>()))
                .ConfigureAwait(false);

            e.Should().BeOfType<ConflictResolutionFailedException>();
        }
        [Fact]
        async Task IgnoreConflictResolverWritesEvents()
        {
            var store = Fake<IStoreEvents>();
            var sut = new IgnoreConflictResolver(store, Fake<StreamIdGenerator>());

            await sut.Resolve<Fakes, FakeState>(Fake<Fakes>(), Fake<IFullEvent[]>(), Fake<Guid>(), Fake<Dictionary<string, string>>()).ConfigureAwait(false);

            A.CallTo(() => 
                store.WriteEvents<Fakes>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored))
                .Should().HaveHappened();
        }
        [Fact]
        async Task DiscardConflictResolverDoesntThrowOrSave()
        {
            var store = Fake<IStoreEvents>();
            var sut = new DiscardConflictResolver();

            await sut.Resolve<Fakes, FakeState>(Fake<Fakes>(), Fake<IFullEvent[]>(), Fake<Guid>(), Fake<Dictionary<string, string>>()).ConfigureAwait(false);

            A.CallTo(() =>
                store.WriteEvents<Fakes>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored))
                .Should().NotHaveHappened();
        }
        

    }
}
