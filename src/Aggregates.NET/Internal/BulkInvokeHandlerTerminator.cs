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
using Metrics;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class DelayedMessage
    {
        public string MessageId { get; set; }
        public IDictionary<string, string> Headers { get; set; }
        public object Message { get; set; }
        public DateTime Received { get; set; }
        public String ChannelKey { get; set; }
    }
    internal class BulkInvokeHandlerTerminator : PipelineTerminator<IInvokeHandlerContext>
    {
        private static readonly Meter InvokesDelayed = Metric.Meter("Delayed Invokes", Unit.Items, tags: "debug");
        private static readonly Meter Invokes = Metric.Meter("Bulk Invokes", Unit.Items);
        private static readonly ILog Logger = LogManager.GetLogger("BulkInvokeHandlerTerminator");

        private static readonly ConcurrentDictionary<string, DelayedAttribute> IsDelayed = new ConcurrentDictionary<string, DelayedAttribute>();
        private static readonly object Lock = new Object();
        private static readonly HashSet<string> IsNotDelayed = new HashSet<string>();

        private readonly IMessageMapper _mapper;

        public BulkInvokeHandlerTerminator(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        protected override async Task Terminate(IInvokeHandlerContext context)
        {
            var channel = context.Builder.Build<IDelayedChannel>();

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
            
            // Special case for when we are bulk processing messages from DelayedSubscriber, simply process it and return dont check for more bulk
            if (contains || (context.Headers.ContainsKey(Defaults.ChannelKey) && context.Headers[Defaults.ChannelKey] == channelKey))
            {
                Logger.Write(LogLevel.Debug, () => $"Invoking handle for message {msgType.FullName} on handler {messageHandler.HandlerType.FullName}");
                await messageHandler.Invoke(context.MessageBeingHandled, context).ConfigureAwait(false);
                return;
            }
            if (IsDelayed.ContainsKey(channelKey))
            {
                DelayedAttribute delayed;
                IsDelayed.TryGetValue(channelKey, out delayed);

                var msgPkg = new DelayedMessage
                {
                    MessageId = context.MessageId,
                    Headers = context.Headers,
                    Message = context.MessageBeingHandled,
                    Received = DateTime.UtcNow,
                    ChannelKey = channelKey
                };

                InvokesDelayed.Mark();

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

                Logger.Write(LogLevel.Debug, () => $"Delaying message {msgType.FullName} delivery");
                await channel.AddToQueue(channelKey, msgPkg, key: specificKey).ConfigureAwait(false);

                bool bulkInvoked;
                if (context.Extensions.TryGet<bool>("BulkInvoked", out bulkInvoked) && bulkInvoked)
                {
                    // Prevents a single message from triggering a dozen different bulk invokes
                    Logger.Write(LogLevel.Debug, () => $"Limiting bulk processing for a single message to a single invoke");
                    return;
                }


                int? size = null;
                TimeSpan? age = null;

                if (delayed.Delay.HasValue)
                    age = await channel.Age(channelKey, key: specificKey).ConfigureAwait(false);
                if (delayed.Count.HasValue)
                    size = await channel.Size(channelKey, key: specificKey).ConfigureAwait(false);


                if (!ShouldExecute(delayed, size, age))
                {
                    Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] Size [{size}] Age [{age?.TotalMilliseconds}] - delaying processing channel [{channelKey}]");

                    return;
                }

                context.Extensions.Set<bool>("BulkInvoked", true);

                Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] Size [{size}] Age [{age?.TotalMilliseconds}] - bulk processing channel [{channelKey}]");

                await InvokeDelayedChannel(channel, channelKey, specificKey, delayed, messageHandler, context).ConfigureAwait(false);

                return;
            }

            var attrs =
                Attribute.GetCustomAttributes(messageHandler.HandlerType, typeof(DelayedAttribute))
                    .Cast<DelayedAttribute>();
            var single = attrs.SingleOrDefault(x => x.Type == msgType);
            if (single == null)
            {
                lock (Lock) IsNotDelayed.Add(channelKey);
            }
            else
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Found delayed handler {messageHandler.HandlerType.FullName} for message {msgType.FullName}");
                IsDelayed.TryAdd(channelKey, single);
            }
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

            if (!msgs.Any())
            {
                Logger.Write(LogLevel.Debug, () => $"No delayed events found on channel [{channelKey}] specific key [{specificKey}]");
                return;
            }

            Invokes.Mark();
            var idx = 0;
            var count = msgs.Count();
            Logger.Write(LogLevel.Debug, () => $"Starting invoke handle {count} times channel key [{channelKey}] specific key [{specificKey}]");
            foreach (var msg in msgs.Cast<DelayedMessage>())
            {
                idx++;
                Logger.Write(LogLevel.Debug, () => $"Invoking handle {idx}/{count} times channel key [{channelKey}] specific key [{specificKey}]");
                await handler.Invoke(msg.Message, context).ConfigureAwait(false);
            }
            Logger.Write(LogLevel.Debug, () => $"Finished invoke handle {count} times channel key [{channelKey}] specific key [{specificKey}]");
        }


        public class State
        {
            public bool ScopeWasPresent { get; set; }
        }
    }
}
