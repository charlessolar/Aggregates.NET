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
    }
    internal class BulkInvokeHandlerTerminator : PipelineTerminator<IInvokeHandlerContext>
    {
        private static readonly Meter InvokesDelayed = Metric.Meter("Delayed Invokes", Unit.Items);
        private static readonly Meter Invokes = Metric.Meter("Bulk Invokes", Unit.Items);
        private static readonly ILog Logger = LogManager.GetLogger("BulkInvokeHandlerTerminator");
        
        private static readonly ConcurrentDictionary<string, DelayedAttribute> IsDelayed = new ConcurrentDictionary<string, DelayedAttribute>();
        private static readonly object Lock = new Object();
        private static readonly HashSet<string> IsNotDelayed = new HashSet<string>();

        private readonly IMessageMapper _mapper;
        private readonly int _maxPulledDelayed;

        public BulkInvokeHandlerTerminator(IMessageMapper mapper, int maxPulledDelayed)
        {
            _mapper = mapper;
            _maxPulledDelayed = maxPulledDelayed;
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


            var key = $"{messageHandler.HandlerType.FullName}:{msgType.FullName}";

            bool contains = false;
            lock (Lock) contains = IsNotDelayed.Contains(key);

            if (contains)
            {
                Logger.Write(LogLevel.Debug, () => $"Invoking handle for message {msgType.FullName} on handler {messageHandler.HandlerType.FullName}");
                await messageHandler.Invoke(context.MessageBeingHandled, context).ConfigureAwait(false);
                return;
            }
            if (IsDelayed.ContainsKey(key))
            {
                DelayedAttribute delayed;
                IsDelayed.TryGetValue(key, out delayed);

                var msgPkg = new DelayedMessage
                {
                    MessageId = context.MessageId,
                    Headers = context.Headers,
                    Message = context.MessageBeingHandled,
                    Received = DateTime.UtcNow,
                };

                InvokesDelayed.Mark();

                var channelKey = key;
                if (delayed.KeyPropertyFunc != null)
                {
                    try
                    {
                        channelKey = $"{key}:{delayed.KeyPropertyFunc(context.MessageBeingHandled)}";
                    }
                    catch
                    {
                        Logger.Warn($"Failed to get key properties from message {msgType.FullName}");
                    }
                }

                Logger.Write(LogLevel.Debug, () => $"Delaying message {msgType.FullName} delivery");
                await channel.AddToQueue(channelKey, msgPkg).ConfigureAwait(false);

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
                    age = await channel.Age(channelKey).ConfigureAwait(false);
                if (delayed.Count.HasValue)
                    size = await channel.Size(channelKey).ConfigureAwait(false);


                if (!ShouldExecute(delayed, size, age))
                {
                    Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] Size [{size}] Age [{age?.TotalMilliseconds}] - delaying processing channel [{channelKey}]");

                    // Even if we dont see a message with specific [channelKey] again we'll need to pull the channel eventually if [Age] is used
                    // DelayedExpirations keeps track of this
                    if (delayed.Delay.HasValue)
                    {
                        ExpiringBulkInvokes.Add(key, channelKey, TimeSpan.FromMilliseconds(delayed.Delay.Value));

                        Logger.Write(LogLevel.Debug, () => $"Checking for channel expirations on key {key}");
                        var expiredChannels = await channel.Pull(key, max: 1).ConfigureAwait(false);

                        foreach (var expired in expiredChannels.Cast<string>())
                        {
                            Logger.Write(LogLevel.Debug,
                                () =>
                                        $"Found expired channel {expired} - bulk processing message {msgType.FullName} on handler {messageHandler.HandlerType.FullName}");
                            await
                                InvokeDelayedChannel(channel, expired, delayed, messageHandler, context)
                                    .ConfigureAwait(false);
                        }
                    }

                    return;
                }

                context.Extensions.Set<bool>("BulkInvoked", true);

                Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] Size [{size}] Age [{age?.TotalMilliseconds}] - bulk processing channel [{channelKey}]");

                await InvokeDelayedChannel(channel, channelKey, delayed, messageHandler, context).ConfigureAwait(false);

                return;
            }

            var attrs =
                Attribute.GetCustomAttributes(messageHandler.HandlerType, typeof(DelayedAttribute))
                    .Cast<DelayedAttribute>();
            var single = attrs.SingleOrDefault(x => x.Type == msgType);
            if (single == null)
            {
                lock (Lock) IsNotDelayed.Add(key);
            }
            else
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Found delayed handler {messageHandler.HandlerType.FullName} for message {msgType.FullName}");
                IsDelayed.TryAdd(key, single);
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

        private async Task InvokeDelayedChannel(IDelayedChannel channel, string channelKey, DelayedAttribute attr, MessageHandler handler, IInvokeHandlerContext context)
        {
            var pull = _maxPulledDelayed;
            if (attr.MaxPull.HasValue)
                pull = Math.Min(attr.MaxPull.Value, _maxPulledDelayed);

            var msgs = await channel.Pull(channelKey, max: pull).ConfigureAwait(false);

            if (!msgs.Any())
            {
                Logger.Write(LogLevel.Debug, () => $"No delayed events found on channel [{channelKey}]");
                return;
            }
            
            Invokes.Mark();
            var idx = 0;
            foreach (var msg in msgs.Cast<DelayedMessage>())
            {
                idx++;
                Logger.Write(LogLevel.Debug, () => $"Invoking handle {idx}/{msgs.Count()} times channel key {channelKey}");
                await handler.Invoke(msg.Message, context).ConfigureAwait(false);
            }
        }


        public class State
        {
            public bool ScopeWasPresent { get; set; }
        }
    }
}
