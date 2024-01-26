using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using NServiceBus;
using NServiceBus.Testing;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class MessageIdentifier : TestSubject<Internal.MessageIdentifier>
    {

        public MessageIdentifier()
        {
            // evil hack
            //bool IsMessageType(Type t) => true;
            //var messageMetadataRegistry = (MessageMetadataRegistry)Activator.CreateInstance(
            //    type: typeof(MessageMetadataRegistry),
            //    bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance,
            //    binder: null,
            //    args: new object[] { (Func<Type, bool>)IsMessageType },
            //    culture: CultureInfo.InvariantCulture);
            //Inject(messageMetadataRegistry);
        }

        [Fact]
        public async Task NoEnclosedMessageType()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingPhysicalMessageContext();
            context.UpdateMessage(new byte[0]);

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
        }

        [Fact]
        public async Task TranslateEnclosedType()
        {
            var registrar = Fake<IVersionRegistrar>();
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingPhysicalMessageContext();
            context.UpdateMessage(new byte[0]);
            context.MessageHeaders.Add(Headers.EnclosedMessageTypes, "xx");

            A.CallTo(() => registrar.GetNamedType("xx")).Returns(typeof(FakeDomainEvent.FakeEvent));

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
            context.Message.Headers[Headers.EnclosedMessageTypes].Should().Be($"{Sut.SerializeEnclosedMessageTypes(typeof(FakeDomainEvent.FakeEvent))}");
        }
        [Fact]
        public async Task NoMappedType()
        {
            var registrar = Fake<IVersionRegistrar>();
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingPhysicalMessageContext();
            context.UpdateMessage(new byte[0]);
            context.MessageHeaders.Add(Headers.EnclosedMessageTypes, "xx");

            A.CallTo(() => registrar.GetNamedType("xx")).Returns(null);

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
            context.Message.Headers.Should().NotContainKey(Headers.EnclosedMessageTypes);
        }
    }
}
