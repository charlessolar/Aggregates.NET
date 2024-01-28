using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Routing;
using NServiceBus.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class Dispatcher : Contracts.IMessageDispatcher
    {
        private readonly ILogger Logger;
        private readonly IMessageSerializer _serializer;
        private readonly IEventMapper _mapper;
        private readonly IVersionRegistrar _registrar;
        private readonly NServiceBus.Transport.IMessageDispatcher _dispatcher;
        private readonly ReceiveAddresses _receiveAddresses;

        public Dispatcher(ILogger<Dispatcher> logger, NServiceBus.Transport.IMessageDispatcher dispatcher, IMessageSerializer serializer, IEventMapper mapper, IVersionRegistrar registrar, ReceiveAddresses receiveAddresses)
        {
            Logger = logger;
            _dispatcher = dispatcher;
            _serializer = serializer;
            _mapper = mapper;
            _registrar = registrar;
            _receiveAddresses = receiveAddresses;
        }



        public async Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null)
        {
            headers = headers ?? new Dictionary<string, string>();

			var finalHeaders = message.Headers.Merge(headers);

            // prioritize the event type from EventStore if we have it
            if (!finalHeaders.TryGetValue($"{Defaults.PrefixHeader}.EventType", out var messageType)) {

                var objectType = message.Message.GetType();
                if (!objectType.IsInterface)
					objectType = _mapper.GetMappedTypeFor(objectType) ?? objectType;
                messageType = _registrar.GetVersionedName(objectType);

			}
            finalHeaders[Headers.EnclosedMessageTypes] = messageType;
            finalHeaders[Headers.MessageIntent] = MessageIntent.Send.ToString();


            var messageId = Guid.NewGuid().ToString();
            var corrId = "";
            if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"))
                messageId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"];
			if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.EventIdHeader}"))
				messageId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.EventIdHeader}"];
			if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"))
                corrId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"];

            finalHeaders[Headers.MessageId] = messageId;
            finalHeaders[Headers.CorrelationId] = corrId;

            var bytes = _serializer.Serialize(message.Message);
            var messageBytes = new ReadOnlyMemory<byte>(bytes);

            var request = new OutgoingMessage(
                messageId: messageId,
                headers: finalHeaders,
                body: messageBytes);
            var operation = new TransportOperation(
                request,
                new UnicastAddressTag(_receiveAddresses.InstanceReceiveAddress));

            Logger.DebugEvent("SendLocal", "Starting local message {messageType} [{MessageId:l}] Corr [{CorrelationId:l}]", messageType, messageId, corrId);
            await _dispatcher.Dispatch(
                outgoingMessages: new TransportOperations(operation),
                transaction: new TransportTransaction())
            .ConfigureAwait(false);

        }

    }
}
