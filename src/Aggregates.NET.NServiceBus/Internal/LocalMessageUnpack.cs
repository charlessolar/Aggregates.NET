using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    public class LocalMessageUnpack : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("LocalMessageUnpack");

        private readonly IMetrics _metrics;

        public LocalMessageUnpack(IMetrics metrics)
        {
            _metrics = metrics;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var originalheaders = context.MessageHeaders.ToDictionary(kv => kv.Key, kv => kv.Value);
            var originalInstance = context.Message.Instance;

            // Stupid hack to get events from ES and messages from NSB into the same pipeline
            // Special case for delayed messages read from delayed stream
            if (context.Extensions.TryGet(Defaults.BulkHeader, out IFullMessage[] delayedMessages))
            {
                _metrics.Mark("Messages", Unit.Message, delayedMessages.Length);

                Logger.DebugEvent("Bulk", "Processing {Count}", delayedMessages.Length);
                var index = 1;

                try
                {
                    foreach (var x in delayedMessages)
                    {
                        // Replace all headers with the original headers to preserve CorrId etc.
                        context.Headers.Clear();
                        foreach (var header in x.Headers)
                            context.Headers[header.Key] = header.Value;
                        
                        context.Headers[Defaults.BulkHeader] = delayedMessages.Length.ToString();
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
                    context.UpdateMessageInstance(originalInstance);
                }
            }
            else if (context.Message.MessageType == typeof(BulkMessage))
            {
                var bulk = context.Message.Instance as BulkMessage;
                // A bulk message thats retried will be in extensions LocalHeader
                if (context.Extensions.TryGet(Defaults.LocalHeader, out object local))
                    bulk = local as BulkMessage;

                _metrics.Mark("Messages", Unit.Message, bulk.Messages.Length);
                Logger.DebugEvent("Bulk", "Processing {Count} [{MessageId:l}]", bulk.Messages.Length, context.MessageId);

                var index = 1;
                try
                {
                    foreach (var x in bulk.Messages)
                    {
                        // Replace all headers with the original headers to preserve CorrId etc.
                        context.Headers.Clear();
                        foreach (var header in x.Headers)
                            context.Headers[header.Key] = header.Value;
                        
                        context.Headers[Defaults.BulkHeader] = bulk.Messages.Length.ToString();

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
                    context.UpdateMessageInstance(originalInstance);
                }
            }
            else if (context.Extensions.TryGet(Defaults.LocalHeader, out object @event))
            {
                try
                {
                    _metrics.Mark("Messages", Unit.Message);
                    context.UpdateMessageInstance(@event);
                    await next().ConfigureAwait(false);
                }
                finally
                {
                    context.UpdateMessageInstance(originalInstance);
                }
            }
            else
            {
                _metrics.Mark("Messages", Unit.Message);
                await next().ConfigureAwait(false);
            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class LocalMessageUnpackRegistration : RegisterStep
    {
        public LocalMessageUnpackRegistration() : base(
            stepId: "LocalMessageUnpack",
            behavior: typeof(LocalMessageUnpack),
            description: "Pulls local message from context",
            factoryMethod: (b) => new LocalMessageUnpack(b.Build<IMetrics>())
        )
        {
            InsertAfterIfExists("UnitOfWorkExecution");
        }
    }
}
