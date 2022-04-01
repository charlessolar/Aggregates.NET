using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class MessageDetyper : TestSubject<Internal.MessageDetyper>
    {

        [Fact]
        public async Task NoEnclosedMessageType()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingPhysicalMessageContext();
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task FailToGetMessageType()
        {
            var registrar = Fake<IVersionRegistrar>();
            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingPhysicalMessageContext();
            context.Headers.Add(Headers.EnclosedMessageTypes, "xx");
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            A.CallTo(() => registrar.GetVersionedName(A<Type>.Ignored)).MustNotHaveHappened();
            context.Headers[Headers.EnclosedMessageTypes].Should().Be("xx");
        }
        [Fact]
        public async Task ReplacedMessageTypeWithVersioned()
        {
            var registrar = Fake<IVersionRegistrar>();
            var mapper = Fake<IEventMapper>();

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingPhysicalMessageContext();

            A.CallTo(() => mapper.GetMappedTypeFor(typeof(FakeDomainEvent.FakeEvent))).Returns(null);
            A.CallTo(() => registrar.GetVersionedName(typeof(FakeDomainEvent.FakeEvent))).Returns("test");

            context.Headers.Add(Headers.EnclosedMessageTypes, typeof(FakeDomainEvent.FakeEvent).AssemblyQualifiedName.ToString());
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            context.Headers[Headers.EnclosedMessageTypes].Should().Be("test");
        }
        [Fact]
        public async Task MultipleMessageTypesHandled()
        {
            var registrar = Fake<IVersionRegistrar>();
            var mapper = Fake<IEventMapper>();

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingPhysicalMessageContext();

            A.CallTo(() => mapper.GetMappedTypeFor(typeof(FakeDomainEvent.FakeEvent))).Returns(null);
            A.CallTo(() => registrar.GetVersionedName(typeof(FakeDomainEvent.FakeEvent))).Returns("test");

            context.Headers.Add(Headers.EnclosedMessageTypes, $"{typeof(FakeDomainEvent.FakeEvent).AssemblyQualifiedName};xxx");
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            context.Headers[Headers.EnclosedMessageTypes].Should().Be("test");
        }
        [Fact]
        public async Task InvalidMultipleMessageType()
        {
            var registrar = Fake<IVersionRegistrar>();
            var mapper = Fake<IEventMapper>();

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingPhysicalMessageContext();

            A.CallTo(() => mapper.GetMappedTypeFor(typeof(FakeDomainEvent.FakeEvent))).Returns(null);
            A.CallTo(() => registrar.GetVersionedName(typeof(FakeDomainEvent.FakeEvent))).Returns("test");

            context.Headers.Add(Headers.EnclosedMessageTypes, $"xxx;{typeof(FakeDomainEvent.FakeEvent).AssemblyQualifiedName}");
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            A.CallTo(() => registrar.GetVersionedName(A<Type>.Ignored)).MustNotHaveHappened();
            context.Headers[Headers.EnclosedMessageTypes].Should().Be($"xxx;{typeof(FakeDomainEvent.FakeEvent).AssemblyQualifiedName}");
        }
        [Fact]
        public async Task NoVersionedName()
        {
            var registrar = Fake<IVersionRegistrar>();
            var mapper = Fake<IEventMapper>();

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingPhysicalMessageContext();

            A.CallTo(() => mapper.GetMappedTypeFor(typeof(FakeDomainEvent.FakeEvent))).Returns(null);
            A.CallTo(() => registrar.GetVersionedName(typeof(FakeDomainEvent.FakeEvent))).Returns(null);

            context.Headers.Add(Headers.EnclosedMessageTypes, $"{typeof(FakeDomainEvent.FakeEvent).AssemblyQualifiedName}");
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            context.Headers[Headers.EnclosedMessageTypes].Should().Be($"{typeof(FakeDomainEvent.FakeEvent).AssemblyQualifiedName}");
        }

    }
}
