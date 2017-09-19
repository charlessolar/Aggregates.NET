using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class Dispatcher : IMessageDispatcher
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Dispatcher");
        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IMessageSession _bus;
        private readonly IEventMapper _mapper;
        
        // A fake message that will travel through the pipeline in order to process events from the context bag
        private static readonly byte[] Marker = new byte[] { 0x7b, 0x7d };

        public Dispatcher(IMetrics metrics, IMessageSerializer serializer, IMessageSession bus, IEventMapper mapper)
        {
            _metrics = metrics;
            _serializer = serializer;
            _bus = bus;
            _mapper = mapper;
        }

        public Task Send(IFullMessage message, string destination, IDictionary<string, string> headers = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Sending message of type [{message.Message.GetType().FullName}]");

            var options = new SendOptions();
            options.SetDestination(destination);

            if (headers != null)
                foreach (var header in headers)
                    options.SetHeader(header.Key, header.Value);

            _metrics.Mark("Dispatched Messages", Unit.Message);
            return _bus.Send(message.Message, options);
        }

        public async Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Sending local message of type [{message.Message.GetType().FullName}]");

            while (!Bus.BusOnline)
                await Task.Delay(100).ConfigureAwait(false);

            var contextBag = new ContextBag();
            // Hack to get all the events to invoker without NSB deserializing 
            contextBag.Set(Defaults.LocalHeader, message.Message);


            var processed = false;
            var numberOfDeliveryAttempts = 0;

            var messageId = Guid.NewGuid().ToString();
            var corrId = "";
            if (message?.Headers?.ContainsKey(Headers.CorrelationId) ?? false)
                corrId = message.Headers[Headers.CorrelationId];

            while (!processed)
            {
                var transportTransaction = new TransportTransaction();
                var tokenSource = new CancellationTokenSource();

                var messageType = message.Message.GetType();
                if (!messageType.IsInterface)
                    messageType = _mapper.GetMappedTypeFor(messageType) ?? messageType;

                var finalHeaders = message.Headers.Merge(headers ?? new Dictionary<string, string>());
                finalHeaders[Headers.EnclosedMessageTypes] = messageType.AssemblyQualifiedName;
                finalHeaders[Headers.MessageIntent] = MessageIntentEnum.Send.ToString();
                finalHeaders[Headers.MessageId] = messageId;
                finalHeaders[Headers.CorrelationId] = corrId;


                try
                {

                    // Don't re-use the event id for the message id
                    var messageContext = new MessageContext(messageId,
                        finalHeaders,
                        Marker, transportTransaction, tokenSource,
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

                    ++numberOfDeliveryAttempts;

                    // Don't retry a cancelation
                    if (tokenSource.IsCancellationRequested)
                        numberOfDeliveryAttempts = Int32.MaxValue;

                    var messageBytes = _serializer.Serialize(message.Message);

                    var errorContext = new ErrorContext(ex, finalHeaders,
                        messageId,
                        messageBytes, transportTransaction,
                        numberOfDeliveryAttempts);
                    if (await Bus.OnError(errorContext).ConfigureAwait(false) ==
                        ErrorHandleResult.Handled || tokenSource.IsCancellationRequested)
                        break;
                }


            }
        }

        public async Task SendLocal(IFullMessage[] messages, IDictionary<string, string> headers = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Sending {messages.Length} bulk local messages");

            while (!Bus.BusOnline)
                await Task.Delay(100).ConfigureAwait(false);

            var contextBag = new ContextBag();
            // Hack to get all the events to invoker without NSB deserializing 
            contextBag.Set(Defaults.LocalBulkHeader, messages);


            var processed = false;
            var numberOfDeliveryAttempts = 0;

            var messageId = Guid.NewGuid().ToString();

            while (!processed)
            {
                var transportTransaction = new TransportTransaction();
                var tokenSource = new CancellationTokenSource();

                var messageType = messages[0].Message.GetType();
                if (!messageType.IsInterface)
                    messageType = _mapper.GetMappedTypeFor(messageType) ?? messageType;

                var finalHeaders = headers.Merge(new Dictionary<string, string>() {
                    [Headers.EnclosedMessageTypes] = messageType.AssemblyQualifiedName,
                    [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                    [Headers.MessageId] = messageId
                });


                try
                {

                    // Don't re-use the event id for the message id
                    var messageContext = new MessageContext(messageId,
                        finalHeaders,
                        Marker, transportTransaction, tokenSource,
                        contextBag);
                    await Bus.OnMessage(messageContext).ConfigureAwait(false);
                    _metrics.Mark("Dispatched Messages", Unit.Message, messages.Length);
                    processed = true;
                }
                catch (ObjectDisposedException)
                {
                    // NSB transport has been disconnected
                    throw new OperationCanceledException();
                }
                catch (Exception ex)
                {
                    _metrics.Mark("Dispatched Errors", Unit.Errors, messages.Length);

                    ++numberOfDeliveryAttempts;

                    // Don't retry a cancelation
                    if (tokenSource.IsCancellationRequested)
                        numberOfDeliveryAttempts = Int32.MaxValue;

                    var messageList = messages.ToList();
                    foreach (var message in messages)
                    {
                        var messageBytes = _serializer.Serialize(message.Message);
                        var errorContext = new ErrorContext(ex, message.Headers.Merge(finalHeaders),
                            messageId,
                            messageBytes, transportTransaction,
                            numberOfDeliveryAttempts);
                        if (await Bus.OnError(errorContext).ConfigureAwait(false) == ErrorHandleResult.Handled)
                            messageList.Remove(message);
                    }
                    messages = messageList.ToArray();
                    if (messages.Length == 0)
                        break;
                }


            }
        }
    }
}
