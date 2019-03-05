using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
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
        private static readonly ILog Logger = LogProvider.GetLogger("Dispatcher");
        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IEventMapper _mapper;
        private readonly IVersionRegistrar _registrar;

        // A fake message that will travel through the pipeline in order to process events from the context bag
        private static readonly byte[] Marker = new byte[] { 0x7b, 0x7d };

        public Dispatcher(IMetrics metrics, IMessageSerializer serializer, IEventMapper mapper, IVersionRegistrar registrar)
        {
            _metrics = metrics;
            _serializer = serializer;
            _mapper = mapper;
            _registrar = registrar;
        }

        public Task Publish(IFullMessage[] messages)
        {
            var options = new PublishOptions();
            _metrics.Mark("Dispatched Messages", Unit.Message);

            // Todo: publish would only be called for messages on a single stream
            // we can set a routing key somehow for BulkMessage so its routed to the same sharded queue 
            var message = new BulkMessage
            {
                Messages = messages
            };
            // Publishing an IMessage normally creates a warning
            options.DoNotEnforceBestPractices();

            return Bus.Instance.Publish(message, options);
        }

        public Task Send(IFullMessage[] messages, string destination)
        {
            var options = new SendOptions();
            options.SetDestination(destination);

            var message = new BulkMessage
            {
                Messages = messages
            };

            _metrics.Mark("Dispatched Messages", Unit.Message);
            return Bus.Instance.Send(message, options);
        }


        public async Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null)
        {
            while (!Bus.BusOnline)
                await Task.Delay(100).ConfigureAwait(false);

            headers = headers ?? new Dictionary<string, string>();

            var contextBag = new ContextBag();
            // Hack to get all the events to invoker without NSB deserializing 
            contextBag.Set(Defaults.LocalHeader, message.Message);


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

        public async Task SendLocal(IFullMessage[] messages, IDictionary<string, string> headers = null)
        {
            while (!Bus.BusOnline)
                await Task.Delay(100).ConfigureAwait(false);

            headers = headers ?? new Dictionary<string, string>();

            await messages.GroupBy(x => x.Message.GetType()).ToArray().StartEachAsync(3, async (group) =>
            {
                var groupedMessages = group.ToArray();

                var contextBag = new ContextBag();
                // Hack to get all the events to invoker without NSB deserializing 
                contextBag.Set(Defaults.BulkHeader, groupedMessages);


                var processed = false;
                var numberOfDeliveryAttempts = 0;

                var messageId = Guid.NewGuid().ToString();

                while (!processed)
                {
                    var transportTransaction = new TransportTransaction();
                    var tokenSource = new CancellationTokenSource();

                    var messageType = group.Key;
                    if (!messageType.IsInterface)
                        messageType = _mapper.GetMappedTypeFor(messageType) ?? messageType;

                    var finalHeaders = headers.Merge(new Dictionary<string, string>()
                    {
                        [Headers.EnclosedMessageTypes] = _registrar.GetVersionedName(messageType),
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
                        _metrics.Mark("Dispatched Messages", Unit.Message, groupedMessages.Length);
                        processed = true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // NSB transport has been disconnected
                        throw new OperationCanceledException();
                    }
                    catch (Exception ex)
                    {
                        _metrics.Mark("Dispatched Errors", Unit.Errors, groupedMessages.Length);

                        ++numberOfDeliveryAttempts;

                        // Don't retry a cancelation
                        if (tokenSource.IsCancellationRequested)
                            numberOfDeliveryAttempts = Int32.MaxValue;

                        var messageList = groupedMessages.ToList();
                        foreach (var message in groupedMessages)
                        {
                            var messageBytes = _serializer.Serialize(message.Message);
                            var errorContext = new ErrorContext(ex, message.Headers.Merge(finalHeaders),
                                messageId,
                                messageBytes, transportTransaction,
                                numberOfDeliveryAttempts);
                            if ((await Bus.OnError(errorContext).ConfigureAwait(false)) == ErrorHandleResult.Handled)
                                messageList.Remove(message);
                        }
                        if (messageList.Count == 0)
                            break;
                        groupedMessages = messageList.ToArray();
                    }

                }
            }).ConfigureAwait(false);

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
