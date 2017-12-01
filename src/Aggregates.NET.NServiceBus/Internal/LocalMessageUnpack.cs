using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    class LocalMessageUnpack : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("LocalMessageUnpack");

        private readonly IMetrics _metrics;

        public LocalMessageUnpack(IMetrics metrics)
        {
            _metrics = metrics;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            // Stupid hack to get events from ES and messages from NSB into the same pipeline
            // Special case for delayed messages read from delayed stream
            if (context.Extensions.TryGet(Defaults.LocalBulkHeader, out IFullMessage[] delayedMessages))
            {
                _metrics.Mark("Messages", Unit.Message, delayedMessages.Length);

                Logger.Write(LogLevel.Debug, () => $"Bulk processing {delayedMessages.Length} messages, bulk id {context.MessageId}");
                var index = 1;
                var originalheaders = new Dictionary<string, string>(context.Headers);

                try
                {
                    foreach (var x in delayedMessages)
                    {
                        // Replace all headers with the original headers to preserve CorrId etc.
                        context.Headers.Clear();
                        foreach (var header in x.Headers)
                            context.Headers[$"{Defaults.DelayedPrefixHeader}.{header.Key}"] = header.Value;

                        context.Headers[Defaults.LocalBulkHeader] = delayedMessages.Length.ToString();
                        Logger.Write(LogLevel.Debug, () => $"Processing {index}/{delayedMessages.Length} message, bulk id {context.MessageId}");

                        if (!x.Headers.ContainsKey(Defaults.ChannelKey))
                        {
                            Logger.Write(LogLevel.Warn, $"Received delayed message without a channel key: {context.Headers.AsString()}");
                            continue;
                        }
                        // Don't set on headers because headers are kept with the message through retries, could lead to unexpected results
                        context.Extensions.Set(Defaults.ChannelKey, x.Headers[Defaults.ChannelKey]);

                        context.UpdateMessageInstance(x.Message);
                        await next().ConfigureAwait(false);
                        index++;
                    }
                }
                finally
                {
                    // Restore original message headers
                    context.Headers.Clear();
                    foreach (var original in originalheaders)
                        context.Headers[original.Key] = original.Value;
                }
            }
            else if (context.Extensions.TryGet(Defaults.LocalHeader, out object @event))
            {
                _metrics.Mark("Messages", Unit.Message);
                context.UpdateMessageInstance(@event);
                await next().ConfigureAwait(false);
            }
            else
            {
                _metrics.Mark("Messages", Unit.Message);
                await next().ConfigureAwait(false);
            }
        }
    }
    internal class LocalMessageUnpackRegistration : RegisterStep
    {
        public LocalMessageUnpackRegistration() : base(
            stepId: "LocalMessageUnpack",
            behavior: typeof(LocalMessageUnpack),
            description: "Pulls local message from context"
        )
        {
            InsertAfter("UnitOfWorkExecution");
        }
    }
}
