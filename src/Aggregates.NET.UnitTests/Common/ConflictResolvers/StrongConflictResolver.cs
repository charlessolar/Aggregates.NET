using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Messages;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Xunit;
using FakeItEasy;
using FluentAssertions;
using AutoFixture.Xunit2;
using AutoFixture;

namespace Aggregates.Common.ConflictResolvers
{
    public class ResolveStronglyConflictResolver : Test
    {
        [Fact]
        async Task ShouldResolveConflict()
        {
            var store = Fake<IStoreEntities>();
            var entity = Fake<FakeEntity>();
            var cleanEntity = Fake<FakeEntity>();

            var sut = new Internal.ResolveStronglyConflictResolver(store);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(cleanEntity);

            await sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>())
                .ConfigureAwait(false);

            A.CallTo(() =>
                store.Commit<FakeEntity, FakeState>(cleanEntity, A<Guid>.Ignored, A<Dictionary<string, string>>.Ignored))
                .Should().HaveHappened();
            cleanEntity.State.Conflicts.Should().Be(3);
        }
        [Fact]
        async Task NoRouteExceptionShouldThrowConflictResolutionFailedException()
        {
            var entity = Fake<FakeEntity>();

            var sut = new Internal.ResolveStronglyConflictResolver(Fake<IStoreEntities>());
            entity.ApplyEvents(Many<FakeNotHandledEvent.UnknownEvent>());

            var e = await Record.ExceptionAsync(() => sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>())).ConfigureAwait(false);

            e.Should().BeOfType<ConflictResolutionFailedException>();
        }
        [Fact]
        async Task ShouldThrowAbandonConflictResolutionException()
        {
            var entity = Fake<FakeEntity>();
            // The entity we get back from the store during a conflict
            var cleanEntity = Fake<FakeEntity>();
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(cleanEntity);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            cleanEntity.State.ThrowAbandon = true;

            var sut = new Internal.ResolveStronglyConflictResolver(store);

            var e = await Record.ExceptionAsync(() => sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>())).ConfigureAwait(false);

            e.Should().BeOfType<AbandonConflictException>();
        }
        [Fact]
        async Task ShouldDiscardEventsWhichThrowDiscardEventException()
        {
            var entity = Fake<FakeEntity>();
            // The entity we get back from the store during a conflict
            var cleanEntity = Fake<FakeEntity>();
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(cleanEntity);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            cleanEntity.State.ThrowDiscard = true;

            var sut = new Internal.ResolveStronglyConflictResolver(store);

            await sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>()).ConfigureAwait(false);

            cleanEntity.Uncommitted.Should().HaveCount(0);
        }
        [Fact]
        async Task ShouldIncludeOobEvents()
        {
            var entity = Fake<FakeEntity>();
            // The entity we get back from the store during a conflict
            var cleanEntity = Fake<FakeEntity>();
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(cleanEntity);
            entity.RaiseEvents(Many<FakeOobEvent.FakeEvent>(), "test");

            var sut = new Internal.ResolveStronglyConflictResolver(store);

            await sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>())
                .ConfigureAwait(false);

            cleanEntity.State.Conflicts.Should().Be(0);
            cleanEntity.State.Handles.Should().Be(3);
            cleanEntity.Uncommitted.Where(x => x.Descriptor.StreamType == StreamTypes.OOB).Should().HaveCount(3);
        }
        [Fact]
        async Task ShouldTransferOobParameters()
        {
            var entity = Fake<FakeEntity>();
            var store = Fake<IStoreEntities>();
            // The entity we get back from the store during a conflict
            var cleanEntity = Fake<FakeEntity>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(cleanEntity);
            entity.RaiseEvents(Many<FakeOobEvent.FakeEvent>(), "test", false, 1);

            var sut = new Internal.ResolveStronglyConflictResolver(store);

            await sut.Resolve<FakeEntity, FakeState>(entity, Fake<Guid>(), Fake<Dictionary<string, string>>())
                .ConfigureAwait(false);

            cleanEntity.Uncommitted.Where(x => 
                x.Descriptor.StreamType == StreamTypes.OOB && 
                x.Descriptor.Headers[Defaults.OobTransientKey] == "False" && 
                x.Descriptor.Headers[Defaults.OobDaysToLiveKey] == "1")
                .Should().HaveCount(3);
        }
    }
}
