using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class BulkInvokeHandlerTerminator : PipelineTerminator<IInvokeHandlerContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(BulkInvokeHandlerTerminator));

        private static readonly ConcurrentDictionary<string, DelayedAttribute> IsDelayed = new ConcurrentDictionary<string, DelayedAttribute>();
        private static readonly HashSet<string> IsNotDelayed = new HashSet<string>();

        private readonly IMessageMapper _mapper;
        private readonly IDelayedChannel _channel;

        public BulkInvokeHandlerTerminator(IMessageMapper mapper, IDelayedChannel channel)
        {
            _mapper = mapper;
            _channel = channel;
        }

        protected override async Task Terminate(IInvokeHandlerContext context)
        {
            var msgType = context.MessageBeingHandled.GetType();
            if (!msgType.IsInterface)
                msgType = _mapper.GetMappedTypeFor(msgType);

            context.Extensions.Set(new State
            {
                ScopeWasPresent = Transaction.Current != null
            });

            var messageHandler = context.MessageHandler;

            Logger.Write(LogLevel.Debug, () => $"Invoking handle for message {msgType.FullName} on handler {messageHandler.HandlerType.FullName}");

            var key = $"{messageHandler.HandlerType.FullName}.{msgType.FullName}";
            if (IsNotDelayed.Contains(key))
            {
                await messageHandler.Invoke(context.MessageBeingHandled, context).ConfigureAwait(false);
                return;
            }
            if (IsDelayed.ContainsKey(key))
            {
                Logger.Write(LogLevel.Debug, () => $"Delaying message {msgType.FullName} delivery");
                var size = await _channel.AddToQueue(key, context.MessageBeingHandled).ConfigureAwait(false);

                DelayedAttribute delayed;
                IsDelayed.TryGetValue(key, out delayed);

                if (delayed.Count.HasValue && size <= delayed.Count.Value) return;
                if (delayed.Delay.HasValue)
                {
                    var oldest = await _channel.Age(key).ConfigureAwait(false);
                    if (oldest < TimeSpan.FromMilliseconds(delayed.Delay.Value)) return;
                }
                Logger.Write(LogLevel.Debug, () => $"Threshold hit - bulk processing {msgType.FullName}");
                var msgs = await _channel.Pull(key).ConfigureAwait(false);

                foreach (var msg in msgs)
                    await messageHandler.Invoke(msg, context).ConfigureAwait(false);

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
