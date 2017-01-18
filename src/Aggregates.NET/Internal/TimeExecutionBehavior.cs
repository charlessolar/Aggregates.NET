using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class TimeExecutionBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger("TimeExecutionBehavior");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow");
        private static readonly HashSet<string> SlowCommandTypes = new HashSet<string>();
        private static readonly object SlowLock = new object();
        private readonly int _slowAlert;

        public TimeExecutionBehavior(int slowAlertThreshold)
        {
            _slowAlert = slowAlertThreshold;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var verbose = false;

            string messageTypeIdentifier;
            if (!context.MessageHeaders.TryGetValue(Headers.EnclosedMessageTypes, out messageTypeIdentifier))
                messageTypeIdentifier = "<UNKNOWN>";
            messageTypeIdentifier += ",";
            messageTypeIdentifier = messageTypeIdentifier.Substring(0, messageTypeIdentifier.IndexOf(','));

            try
            {
                if (SlowCommandTypes.Contains(messageTypeIdentifier))
                {
                    lock (SlowLock) SlowCommandTypes.Remove(messageTypeIdentifier);
                    Logger.Write(LogLevel.Info,
                        () => $"Message {messageTypeIdentifier} was previously detected as slow, switching to more verbose logging (for this instance)\nPayload: {Encoding.UTF8.GetString(context.Message.Body)}");
                    Defaults.MinimumLogging.Value = LogLevel.Info;
                    verbose = true;
                }
                
                var start = Stopwatch.GetTimestamp();

                await next().ConfigureAwait(false);

                var end = Stopwatch.GetTimestamp();
                var elapsed = (end - start) * (1.0 / Stopwatch.Frequency) * 1000;

                if (elapsed > _slowAlert)
                {
                    SlowLogger.Write(LogLevel.Warn,
                        () => $" - SLOW ALERT - Processing message {context.MessageId} {messageTypeIdentifier} took {elapsed} ms\nPayload: {Encoding.UTF8.GetString(context.Message.Body)}");
                    if (!verbose)
                        lock (SlowLock) SlowCommandTypes.Add(messageTypeIdentifier);
                }
                else
                    Logger.Write(LogLevel.Debug,
                        () => $"Processing message {context.MessageId} {messageTypeIdentifier} took {elapsed} ms");

            }
            finally
            {
                if (verbose)
                {
                    Logger.Write(LogLevel.Info,
                        () => $"Finished processing message {messageTypeIdentifier} verbosely - resetting log level");
                    Defaults.MinimumLogging.Value = null;
                }
            }
        }
    }
}
