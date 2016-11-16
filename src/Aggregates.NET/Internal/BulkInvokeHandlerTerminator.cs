using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly ILog Logger = LogManager.GetLogger(typeof(BulkInvokeHandlerTerminator));

        private static readonly ConcurrentDictionary<string, DelayedAttribute> IsDelayed = new ConcurrentDictionary<string, DelayedAttribute>();
        private static readonly HashSet<string> IsNotDelayed = new HashSet<string>();

        private static string _bulkedMessageId;

        private readonly IBuilder _builder;
        private readonly IMessageMapper _mapper;

        public BulkInvokeHandlerTerminator(IBuilder builder, IMessageMapper mapper)
        {
            _builder = builder;
            _mapper = mapper;
        }
        
        protected override async Task Terminate(IInvokeHandlerContext context)
        {
            var channel = _builder.Build<IDelayedChannel>();

            var msgType = context.MessageBeingHandled.GetType();
            if (!msgType.IsInterface)
                msgType = _mapper.GetMappedTypeFor(msgType) ?? msgType;

            context.Extensions.Set(new State
            {
                ScopeWasPresent = Transaction.Current != null
            });

            var messageHandler = context.MessageHandler;


            var key = $"{messageHandler.HandlerType.FullName}:{msgType.FullName}";
            if (IsNotDelayed.Contains(key))
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

                if (delayed.KeyPropertyFunc != null)
                {
                    try
                    {
                        key = $"{key}:{delayed.KeyPropertyFunc(context.MessageBeingHandled)}";
                    }
                    catch
                    {
                        Logger.Warn($"Failed to get property key {delayed.KeyProperty} on message {msgType.FullName}");
                    }
                }
                Logger.Write(LogLevel.Debug, () => $"Delaying message {msgType.FullName} delivery");
                var size = await channel.AddToQueue(key, msgPkg).ConfigureAwait(false);

                if (_bulkedMessageId == context.MessageId)
                {
                    // Prevents a single message from triggering a dozen different bulk invokes
                    Logger.Write(LogLevel.Debug, () => $"Limiting bulk processing for a single message to a single invoke");
                    return;
                }


                TimeSpan? age = null;

                if (delayed.Delay.HasValue)
                    age = await channel.Age(key).ConfigureAwait(false);

                if ((delayed.Count.HasValue && size < delayed.Count.Value) || (delayed.Delay.HasValue && age < TimeSpan.FromMilliseconds(delayed.Delay.Value)))
                {
                    Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] not hit Size [{size}] Age [{age?.TotalMilliseconds}] - delaying processing {msgType.FullName}");
                    return;
                }

                _bulkedMessageId = context.MessageId;
                Logger.Write(LogLevel.Debug, () => $"Threshold Count [{delayed.Count}] DelayMs [{delayed.Delay}] hit Size [{size}] Age [{age?.TotalMilliseconds}] - bulk processing {msgType.FullName}");
                var msgs = await channel.Pull(key).ConfigureAwait(false);

                Invokes.Mark();
                Logger.Write(LogLevel.Debug, () => $"Invoking handle {msgs.Count()} times for message {msgType.FullName} on handler {messageHandler.HandlerType.FullName}");
                foreach (var msg in msgs.Cast<DelayedMessage>())
                    await messageHandler.Invoke(msg.Message, context).ConfigureAwait(false);
                
                return;
            }

            var attrs =
                Attribute.GetCustomAttributes(messageHandler.HandlerType, typeof(DelayedAttribute))
                    .Cast<DelayedAttribute>();
            var single = attrs.SingleOrDefault(x => x.Type == msgType);
            if (single == null)
            {
                IsNotDelayed.Add(key);
            }
            else
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Found delayed handler {messageHandler.HandlerType.FullName} for message {msgType.FullName}");
                IsDelayed.TryAdd(key, single);
            }
            await Terminate(context).ConfigureAwait(false);
        }


        public class State
        {
            public bool ScopeWasPresent { get; set; }
        }
    }
}
