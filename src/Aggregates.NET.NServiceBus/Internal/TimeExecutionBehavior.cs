using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    internal class TimeExecutionBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private readonly ILogger Logger;
        private static readonly HashSet<string> SlowCommandTypes = new HashSet<string>();
        private static readonly object SlowLock = new object();
        private readonly TimeSpan? _slowAlert;

        public TimeExecutionBehavior(ILogger<TimeExecutionBehavior> logger, ISettings settings)
        {
            Logger = logger;
            _slowAlert = settings.SlowAlertThreshold;
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
                    Defaults.MinimumLogging.Value = LogLevel.Debug;
                    Logger.InfoEvent("Slow Alarm", "[{MessageId:l}] Enabling verbose logging for {MessageType}", context.MessageId, messageTypeIdentifier);

                    verbose = true;
                }

                var start = Stopwatch.GetTimestamp();

                await next().ConfigureAwait(false);

                var end = Stopwatch.GetTimestamp();
                // support high resolution 
                var elapsed = (end - start) * (1.0d / Stopwatch.Frequency) * 1000;
                Logger.InfoEvent("Timing", "[{MessageId:l}] {MessageType} took {Milliseconds:F3}ms", context.MessageId, messageTypeIdentifier, elapsed);


                if (_slowAlert.HasValue && elapsed > _slowAlert.Value.TotalSeconds)
                {
                    if (!verbose) {
                        Logger.WarnEvent("Slow Alarm", "[{MessageId:l}] {MessageType} took {Milliseconds:F3}ms payload {Payload}", context.MessageId, messageTypeIdentifier, elapsed, Encoding.UTF8.GetString(context.Message.Body.Span).MaxLines(10));

                        lock (SlowLock) SlowCommandTypes.Add(messageTypeIdentifier);
                    }
                }

            }
            finally
            {
                if (verbose)
                    Defaults.MinimumLogging.Value = null;
            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class TimeExecutionRegistration : RegisterStep
    {
        public TimeExecutionRegistration() : base(
            stepId: "Time Execution",
            behavior: typeof(TimeExecutionBehavior),
            description: "htimes message processing and logs slow ones",
            factoryMethod: (b) => new TimeExecutionBehavior(b.GetService<ILogger<TimeExecutionBehavior>>(), b.GetService<ISettings>())
        )
        {
        }
    }
}
