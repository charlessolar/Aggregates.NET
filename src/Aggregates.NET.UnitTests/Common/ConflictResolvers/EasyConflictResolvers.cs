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
using Microsoft.Extensions.Logging;

namespace Aggregates.Common.ConflictResolvers
{
    public class EasyConflictResolvers : Test
    {
        [Fact]
        async Task ThrowConflictResolverThrowsOnConflict()
        {
            var sut = new ThrowConflictResolver();

            var e = await Record.ExceptionAsync(() => 
                sut.Resolve<FakeEntity, FakeState>(Fake<FakeEntity>(), Fake<Guid>(), Fake<Dictionary<string, string>>()))
                .ConfigureAwait(false);

            e.Should().BeOfType<ConflictResolutionFailedException>();
        }
        [Fact]
        async Task DiscardConflictResolverDoesntThrowOrSave()
        {
            var store = Fake<IStoreEvents>();
            var sut = new DiscardConflictResolver(Fake<ILoggerFactory>());

            await sut.Resolve<FakeEntity, FakeState>(Fake<FakeEntity>(), Fake<Guid>(), Fake<Dictionary<string, string>>()).ConfigureAwait(false);

            A.CallTo(() =>
                store.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored))
                .Should().NotHaveHappened();
        }
        [Fact]
        async Task IgnoreConflictResolverWritesEvents()
        {
            var store = Fake<IStoreEvents>();
            var sut = new IgnoreConflictResolver(Fake<ILoggerFactory>(), store, Fake<IOobWriter>());

            await sut.Resolve<FakeEntity, FakeState>(Fake<FakeEntity>(), Fake<Guid>(), Fake<Dictionary<string, string>>()).ConfigureAwait(false);

            A.CallTo(() => 
                store.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored))
                .Should().HaveHappenedOnce();
        }

        [Fact]
        async Task IgnoreConflictResolverWritesOobEvents()
        {
            var store = Fake<IStoreEvents>();
            var oob = Fake<IOobWriter>();
            var entity = Fake<FakeEntity>();
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();

            var sut = new IgnoreConflictResolver(Fake<ILoggerFactory>(), store, oob);
            entity.RaiseEvents(Many<FakeOobEvent.FakeEvent>(3), "test");

            await sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>()).ConfigureAwait(false);

            // No domain events writen
            A.CallTo(() =>
                store.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.That.IsEmpty(), A<Dictionary<string, string>>.Ignored, A<long?>.Ignored))
                .Should().HaveHappenedOnce();
            A.CallTo(() =>
                oob.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.That.Matches(x => x.Length == 3 ), A<Guid>.Ignored, A<Dictionary<string, string>>.Ignored))
                .Should().HaveHappenedOnce();
        }

    }
}
