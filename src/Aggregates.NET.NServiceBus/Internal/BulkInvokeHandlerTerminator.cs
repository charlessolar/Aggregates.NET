using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class DelayedMessage : IDelayedMessage
    {
        public string MessageId { get; set; }
        public IDictionary<string, string> Headers { get; set; }
        public object Message { get; set; }
        public DateTime Received { get; set; }
        public string ChannelKey { get; set; }
        public string SpecificKey { get; set; }
    }
    internal class BulkInvokeHandlerTerminator : PipelineTerminator<IInvokeHandlerContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("BulkInvokeHandler");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");
        
        private static readonly ConcurrentDictionary<string, DelayedAttribute> IsDelayed = new ConcurrentDictionary<string, DelayedAttribute>();
        private static readonly object Lock = new object();
        private static readonly HashSet<string> IsNotDelayed = new HashSet<string>();

        private readonly IEventMapper _mapper;
        private readonly IMetrics _metrics;

        public BulkInvokeHandlerTerminator(IMetrics metrics, IEventMapper mapper)
        {
            _mapper = mapper;
            _metrics = metrics;
        }

        protected override async Task Terminate(IInvokeHandlerContext context)
        {

            IDelayedChannel channel = null;
            try
            {
                IContainer container;
                if (context.Extensions.TryGet<IContainer>(out container))
                    channel = container.Resolve<IDelayedChannel>();
            }
            catch (Exception)
            {
                // Catch in case IDelayedChannel isn't registered which shouldn't happen unless a user registered Consumer without GetEventStore
            }

                var msgType = context.MessageBeingHandled.GetType();
            if (!msgType.IsInterface)
                msgType = _mapper.GetMappedTypeFor(msgType) ?? msgType;

            context.Extensions.Set(new State
            {
                ScopeWasPresent = Transaction.Current != null
            });

            var messageHandler = context.MessageHandler;
            var channelKey = $"{messageHandler.HandlerType.FullName}:{msgType.FullName}";

            bool contains = false;
            lock (Lock) contains = IsNotDelayed.Contains(channelKey);

            context.Extensions.TryGet(Defaults.ChannelKey, out string contextChannelKey);

            // Special case for when we are bulk processing messages from DelayedSubscriber or BulkMessage, simply process it and return dont check for more bulk
            if (channel == null || contains || contextChannelKey == channelKey || context.Headers.ContainsKey(Defaults.BulkHeader))
            {
                await messageHandler.Invoke(context.MessageBeingHandled, context).ConfigureAwait(false);
                return;
            }
            // If we are bulk processing and the above check fails it means the current message handler shouldn't be called
            // because if contextChannelKey is set we are using delayed bulk SendLocal
            // which should only execute the message on the handlers that were marked delayed, the other handlers were already invoked when the message
            // originally came in
            if (!string.IsNullOrEmpty(contextChannelKey))
                return;
            

            if (IsDelayed.ContainsKey(channelKey))
            {
                DelayedAttribute delayed;
                IsDelayed.TryGetValue(channelKey, out delayed);

                
                var specificKey = "";
                if (delayed.KeyPropertyFunc != null)
                {
                    try
                    {
                        specificKey = delayed.KeyPropertyFunc(context.MessageBeingHandled);
                    }
                    catch
                    {
                        Logger.Warn($"Failed to get key properties from message {msgType.FullName}");
                    }
                }

                var msgPkg = new DelayedMessage
                {
                    MessageId = context.MessageId,
                    Headers = context.Headers,
                    Message = context.MessageBeingHandled,
                    Received = DateTime.UtcNow,
                    ChannelKey = channelKey,
                    SpecificKey = specificKey
                };

                await channel.AddToQueue(channelKey, msgPkg, key: specificKey).ConfigureAwait(false);
                Logger.DebugEvent("QueueAdd", "Message added to delayed queue [{Channel:l}] key [{Key:l}]", channelKey, specificKey);

                bool bulkInvoked;
                if (context.Extensions.TryGet<bool>("BulkInvoked", out bulkInvoked) && bulkInvoked)
                {
                    // Prevents a single message from triggering a dozen different bulk invokes
                    return;
                }

                int? size = null;
                TimeSpan? age = null;

                if (delayed.Delay.HasValue)
                    age = await channel.Age(channelKey, key: specificKey).ConfigureAwait(false);
                if (delayed.Count.HasValue)
                    size = await channel.Size(channelKey, key: specificKey).ConfigureAwait(false);
                
                if (!ShouldExecute(delayed, size, age))
                    return;
                                
                context.Extensions.Set<bool>("BulkInvoked", true);
                
                Logger.DebugEvent("Threshold", "Count [{Count}] DelayMs [{DelayMs}] bulk procesing Size [{Size}] Age [{Age}] - [{Channel:l}] key [{Key:l}]", delayed.Count, delayed.Delay, size, age?.TotalMilliseconds, channelKey, specificKey);
                
                await InvokeDelayedChannel(channel, channelKey, specificKey, delayed, messageHandler, context).ConfigureAwait(false);

                return;
            }

            var attrs =
                Attribute.GetCustomAttributes(messageHandler.HandlerType, typeof(DelayedAttribute))
                    .Cast<DelayedAttribute>();
            var single = attrs.SingleOrDefault(x => x.Type == msgType);
            if (single == null)
                lock (Lock) IsNotDelayed.Add(channelKey);
            else
                IsDelayed.TryAdd(channelKey, single);
            
            await Terminate(context).ConfigureAwait(false);
        }

        private bool ShouldExecute(DelayedAttribute attr, int? size, TimeSpan? age)
        {
            if (attr.Count.HasValue && size.HasValue)
                if (attr.Count.Value <= size)
                    return true;
            if (attr.Delay.HasValue && age.HasValue)
                return TimeSpan.FromMilliseconds(attr.Delay.Value) <= age;
            return false;
        }

        private async Task InvokeDelayedChannel(IDelayedChannel channel, string channelKey, string specificKey, DelayedAttribute attr, MessageHandler handler, IInvokeHandlerContext context)
        {
            var msgs = await channel.Pull(channelKey, key: specificKey, max: attr.Count).ConfigureAwait(false);

            var messages = msgs as IDelayedMessage[] ?? msgs.ToArray();
            
            var count = messages.Length;
            
            using (var ctx = _metrics.Begin("Bulk Messages Time"))
            {
                switch (attr.Mode)
                {
                    case DeliveryMode.Single:
                        foreach (var idx in Enumerable.Range(0, messages.Length))
                            await handler.Invoke(messages[idx].Message, context).ConfigureAwait(false);
                        break;
                    case DeliveryMode.First:
                        await handler.Invoke(messages[0].Message, context).ConfigureAwait(false);
                        break;
                    case DeliveryMode.Last:
                        await handler.Invoke(messages[messages.Length-1].Message, context).ConfigureAwait(false);
                        break;
                    case DeliveryMode.FirstAndLast:
                        await handler.Invoke(messages[0].Message, context).ConfigureAwait(false);
                        await handler.Invoke(messages[messages.Length - 1].Message, context).ConfigureAwait(false);
                        break;
                }
                
                if(ctx.Elapsed > TimeSpan.FromSeconds(5))
                    SlowLogger.InfoEvent("Invoked", "{Count} messages channel [{Channel:l}] key [{Key:l}] took {Milliseconds} ms", count, channelKey, specificKey, ctx.Elapsed.TotalMilliseconds);
                Logger.DebugEvent("Invoked", "{Count} messages channel [{Channel:l}] key [{Key:l}] took {Milliseconds} ms", count, channelKey, specificKey, ctx.Elapsed.TotalMilliseconds);

            }
        }


        public class State
        {
            public bool ScopeWasPresent { get; set; }
        }
    }
}
