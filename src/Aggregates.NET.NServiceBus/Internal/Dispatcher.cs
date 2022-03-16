using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Transport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class Dispatcher : IMessageDispatcher
    {
        private readonly ILogger Logger;
        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IEventMapper _mapper;
        private readonly IVersionRegistrar _registrar;

        public Dispatcher(ILoggerFactory logFactory, IMetrics metrics, IMessageSerializer serializer, IEventMapper mapper, IVersionRegistrar registrar)
        {
            Logger = logFactory.CreateLogger("Dispatcher");
            _metrics = metrics;
            _serializer = serializer;
            _mapper = mapper;
            _registrar = registrar;
        }

        public Task Publish(IFullMessage message)
        {
            var options = new PublishOptions();
            _metrics.Mark("Dispatched Messages", Unit.Message);

            // Publishing an IMessage normally creates a warning
            options.DoNotEnforceBestPractices();

            return Bus.Instance.Publish(message, options);
        }

        public Task Send(IFullMessage message, string destination)
        {
            var options = new SendOptions();
            options.SetDestination(destination);

            _metrics.Mark("Dispatched Messages", Unit.Message);
            return Bus.Instance.Send(message, options);
        }


        public async Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null)
        {
            while (!Bus.BusOnline)
                await Task.Delay(100).ConfigureAwait(false);

            headers = headers ?? new Dictionary<string, string>();

            var contextBag = new ContextBag();
            var bytes = _serializer.Serialize(message.Message);

            var processed = false;
            var numberOfDeliveryAttempts = 0;

            var messageType = message.Message.GetType();
            if (!messageType.IsInterface)
                messageType = _mapper.GetMappedTypeFor(messageType) ?? messageType;
            
            var finalHeaders = message.Headers.Merge(headers);
            finalHeaders[Headers.EnclosedMessageTypes] = _registrar.GetVersionedName(messageType);
            finalHeaders[Headers.MessageIntent] = MessageIntentEnum.Send.ToString();


            var messageId = Guid.NewGuid().ToString();
            var corrId = "";
            if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"))
                messageId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"];
            if (finalHeaders.ContainsKey($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"))
                corrId = finalHeaders[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"];

            Logger.DebugEvent("SendLocal", "Starting local message [{MessageId:l}] Corr [{CorrelationId:l}]", messageId, corrId);

            finalHeaders[Headers.MessageId] = messageId;
            finalHeaders[Headers.CorrelationId] = corrId;

            while (!processed)
            {
                var transportTransaction = new TransportTransaction();
                var tokenSource = new CancellationTokenSource();


                try
                {
                    var messageContext = new MessageContext(messageId,
                        finalHeaders,
                        bytes, transportTransaction, tokenSource,
                        contextBag);
                    await Bus.OnMessage(messageContext).ConfigureAwait(false);
                    _metrics.Mark("Dispatched Messages", Unit.Message);
                    processed = true;
                }
                catch (ObjectDisposedException)
                {
                    // NSB transport has been disconnected
                    throw new OperationCanceledException();
                }
                catch (Exception ex)
                {
                    _metrics.Mark("Dispatched Errors", Unit.Errors);
                    Logger.DebugEvent("SendLocalException", ex, "Local message [{MessageId:l}] Corr [{CorrelationId:l}] exception", messageId, corrId);

                    ++numberOfDeliveryAttempts;

                    // Don't retry a cancelation
                    if (tokenSource.IsCancellationRequested)
                        numberOfDeliveryAttempts = Int32.MaxValue;

                    var messageBytes = _serializer.Serialize(message.Message);

                    var errorContext = new ErrorContext(ex, finalHeaders,
                        messageId,
                        messageBytes, transportTransaction,
                        numberOfDeliveryAttempts);
                    if ((await Bus.OnError(errorContext).ConfigureAwait(false)) ==
                        ErrorHandleResult.Handled || tokenSource.IsCancellationRequested)
                        break;
                }


            }
        }

        public async Task SendToError(Exception ex, IFullMessage message)
        {
            var transportTransaction = new TransportTransaction();

            var messageBytes = _serializer.Serialize(message.Message);

            var headers = new Dictionary<string, string>(message.Headers);

            var errorContext = new ErrorContext(ex, headers,
                Guid.NewGuid().ToString(),
                messageBytes, transportTransaction,
                int.MaxValue);
            if ((await Bus.OnError(errorContext).ConfigureAwait(false)) != ErrorHandleResult.Handled)
                throw new InvalidOperationException("Failed to send message error queue");
        }
    }
}
