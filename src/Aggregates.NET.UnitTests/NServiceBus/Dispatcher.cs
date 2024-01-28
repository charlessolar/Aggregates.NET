using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class Dispatcher : TestSubject<Aggregates.Internal.Dispatcher>
    {
        global::NServiceBus.Transport.IMessageDispatcher _messageDispatcher;
        public Dispatcher()
        {
            _messageDispatcher = Fake<global::NServiceBus.Transport.IMessageDispatcher>();
            Inject(_messageDispatcher);
        }


        [Fact]
        public async Task SendLocal_Sets_GoodHeaders()
        {
            var message = Fake<IFullMessage>();

            message.Headers.Add("foo", "bar");

            var capturedOperations = A.Captured<global::NServiceBus.Transport.TransportOperations>();
            var capturedTransaction = A.Captured<global::NServiceBus.Transport.TransportTransaction>();
            A.CallTo(() => _messageDispatcher.Dispatch(capturedOperations._, capturedTransaction._, A<CancellationToken>.Ignored)).DoesNothing();

            await Sut.SendLocal(message);

            capturedOperations.Values.Should().HaveCount(1);
            capturedOperations.Values.First().UnicastTransportOperations.Should().HaveCount(1);
            var sentMessage = capturedOperations.Values.First().UnicastTransportOperations.First().Message;

            sentMessage.Headers.Should().ContainKey("foo");


        }

        [Fact]
        public async Task MessageId_Is_Used()
        {
            var message = Fake<IFullMessage>();

            message.Headers.Add($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}", "test");

            var capturedOperations = A.Captured<global::NServiceBus.Transport.TransportOperations>();
            var capturedTransaction = A.Captured<global::NServiceBus.Transport.TransportTransaction>();
            A.CallTo(() => _messageDispatcher.Dispatch(capturedOperations._, capturedTransaction._, A<CancellationToken>.Ignored)).DoesNothing();

            await Sut.SendLocal(message);

            capturedOperations.Values.Should().HaveCount(1);
            capturedOperations.Values.First().UnicastTransportOperations.Should().HaveCount(1);
            var sentMessage = capturedOperations.Values.First().UnicastTransportOperations.First().Message;

            sentMessage.MessageId.Should().Be("test");
            sentMessage.Headers[Headers.MessageId].Should().Be("test");
        }

        [Fact]
        public async Task EventId_Is_Used()
        {
            var message = Fake<IFullMessage>();

            message.Headers.Add($"{Defaults.PrefixHeader}.{Defaults.EventIdHeader}", "test");

            var capturedOperations = A.Captured<global::NServiceBus.Transport.TransportOperations>();
            var capturedTransaction = A.Captured<global::NServiceBus.Transport.TransportTransaction>();
            A.CallTo(() => _messageDispatcher.Dispatch(capturedOperations._, capturedTransaction._, A<CancellationToken>.Ignored)).DoesNothing();

            await Sut.SendLocal(message);

            capturedOperations.Values.Should().HaveCount(1);
            capturedOperations.Values.First().UnicastTransportOperations.Should().HaveCount(1);
            var sentMessage = capturedOperations.Values.First().UnicastTransportOperations.First().Message;

            sentMessage.MessageId.Should().Be("test");
            sentMessage.Headers[Headers.MessageId].Should().Be("test");
        }
        [Fact]
        public async Task CorrId_Is_Used()
        {
            var message = Fake<IFullMessage>();

            message.Headers.Add($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}", "test");

            var capturedOperations = A.Captured<global::NServiceBus.Transport.TransportOperations>();
            var capturedTransaction = A.Captured<global::NServiceBus.Transport.TransportTransaction>();
            A.CallTo(() => _messageDispatcher.Dispatch(capturedOperations._, capturedTransaction._, A<CancellationToken>.Ignored)).DoesNothing();

            await Sut.SendLocal(message);

            capturedOperations.Values.Should().HaveCount(1);
            capturedOperations.Values.First().UnicastTransportOperations.Should().HaveCount(1);
            var sentMessage = capturedOperations.Values.First().UnicastTransportOperations.First().Message;

            sentMessage.Headers[Headers.CorrelationId].Should().Be("test");
        }
        [Fact]
        public async Task EventType_Header_Is_MessageType()
        {
            var message = Fake<IFullMessage>();

            message.Headers.Add($"{Defaults.PrefixHeader}.EventType", "test");

            var capturedOperations = A.Captured<global::NServiceBus.Transport.TransportOperations>();
            var capturedTransaction = A.Captured<global::NServiceBus.Transport.TransportTransaction>();
            A.CallTo(() => _messageDispatcher.Dispatch(capturedOperations._, capturedTransaction._, A<CancellationToken>.Ignored)).DoesNothing();

            await Sut.SendLocal(message);

            capturedOperations.Values.Should().HaveCount(1);
            capturedOperations.Values.First().UnicastTransportOperations.Should().HaveCount(1);
            var sentMessage = capturedOperations.Values.First().UnicastTransportOperations.First().Message;

            sentMessage.Headers[Headers.EnclosedMessageTypes].Should().Be("test");
        }


    }
}
