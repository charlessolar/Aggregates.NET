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
    internal class DelayedMessage : IDelayedMessage
    {
        public string MessageId { get; set; }
        public IDictionary<string, string> Headers { get; set; }
        public object Message { get; set; }
        public DateTime Received { get; set; }
        public String ChannelKey { get; set; }
    }
    internal class BulkInvokeHandlerTerminator : PipelineTerminator<IInvokeHandlerContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger("BulkInvokeHandlerTerminator");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");

        private static readonly Meter Invokes = Metric.Meter("Bulk Message Invokes", Unit.Items, tags: "debug");
        private static readonly Meter InvokesDelayed = Metric.Meter("Delayed Messages", Unit.Items, tags: "debug");
        private static readonly Histogram InvokeSize = Metric.Histogram("Bulk Messages Size", Unit.Items, tags: "debug");
        private static readonly Metrics.Timer InvokeTime = Metric.Timer("Bulk Messages Time", Unit.None, tags: "debug");

        internal static readonly Dictionary<string, DateTime> RecentlyInvoked = new Dictionary<string, DateTime>();
        private static readonly object RecentLock = new object();
        private static readonly Task Expiring = Timer.Repeat(() =>
        {
            lock (RecentLock)
            {
                var expired = RecentlyInvoked.Where(x => x.Value < DateTime.UtcNow).ToList();
                foreach (var e in expired)
                    RecentlyInvoked.Remove(e.Key);
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(5), "recently invoked eviction");

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

            var contextChannelKey = "";
            if (context.Headers.ContainsKey(Defaults.ChannelKey))
                contextChannelKey = context.Headers[Defaults.ChannelKey];

            // Special case for when we are bulk processing messages from DelayedSubscriber, simply process it and return dont check for more bulk
            if (channel == null || contains || contextChannelKey == channelKey)
            {
                Logger.Write(LogLevel.Debug, () => $"Invoking handle for message {msgType.FullName} on handler {messageHandler.HandlerType.FullName}");
                await messageHandler.Invoke(context.MessageBeingHandled, context).ConfigureAwait(false);
                return;
            }
            // If we are bulk processing and the above check fails it means the current message handler shouldn't be called
            if (!string.IsNullOrEmpty(contextChannelKey))
                return;

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
                
                await channel.AddToQueue(channelKey, msgPkg, key: specificKey).ConfigureAwait(false);

                bool bulkInvoked;
                if (context.Extensions.TryGet<bool>("BulkInvoked", out bulkInvoked) && bulkInvoked)
                {
                    // Prevents a single message from triggering a dozen different bulk invokes
                    Logger.Write(LogLevel.Info, () => $"Limiting bulk processing for a single message to a single invoke");
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
                    Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] Size [{size}] Age [{age?.TotalMilliseconds}] - delaying processing channel [{channelKey}] specific [{specificKey}]");

                    return;
                }

                lock (RecentLock)
                {
                    if (RecentlyInvoked.ContainsKey($"{channelKey}.{specificKey}"))
                    {
                        Logger.Write(LogLevel.Debug, () => $"Channel [{channelKey}] specific [{specificKey}] was checked by this instance recently - leaving it alone");
                        return;
                    }
                    RecentlyInvoked.Add($"{channelKey}.{specificKey}", DateTime.UtcNow.AddSeconds(10));
                }
                context.Extensions.Set<bool>("BulkInvoked", true);

                Logger.Write(LogLevel.Info, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] Size [{size}] Age [{age?.TotalMilliseconds}] - bulk processing channel [{channelKey}] specific [{specificKey}]");
                
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

            var idx = 0;
            var count = msgs.Count();

            Invokes.Mark();
            InvokeSize.Update(msgs.Count());
            Logger.Write(LogLevel.Info, () => $"Starting invoke handle {count} times channel key [{channelKey}] specific key [{specificKey}]");
            using (var ctx = InvokeTime.NewContext())
            {
                foreach (var msg in msgs.Cast<IDelayedMessage>())
                {
                    idx++;
                    Logger.Write(LogLevel.Debug,
                        () => $"Invoking handle {idx}/{count} times channel key [{channelKey}] specific key [{specificKey}]");
                    await handler.Invoke(msg.Message, context).ConfigureAwait(false);
                }
                if(ctx.Elapsed > TimeSpan.FromSeconds(5))
                    SlowLogger.Write(LogLevel.Warn, () => $"Bulk invoking {count} times on channel key [{channelKey}] specific key [{specificKey}] took {ctx.Elapsed.TotalSeconds} seconds!");
                Logger.Write(LogLevel.Info, () => $"Bulk invoking {count} times on channel key [{channelKey}] specific key [{specificKey}] took {ctx.Elapsed.TotalMilliseconds} ms!");
            }
            Logger.Write(LogLevel.Info, () => $"Finished invoke handle {count} times channel key [{channelKey}] specific key [{specificKey}]");
        }


        public class State
        {
            public bool ScopeWasPresent { get; set; }
        }
    }
}
