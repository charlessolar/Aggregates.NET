using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Pipeline;

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
            if(!_slowAlert.HasValue)
            {
                await next().ConfigureAwait(false);
                return;
            }

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
                    verbose = true;
                }
                
                var start = Stopwatch.GetTimestamp();

                await next().ConfigureAwait(false);

                var end = Stopwatch.GetTimestamp();
                var elapsed = (end - start) * (1.0 / Stopwatch.Frequency) * 1000;

                if (elapsed > _slowAlert.Value.TotalSeconds)
                {
                    Logger.WarnEvent("Slow Alarm", "[{MessageId:l}] {MessageType} took {Milliseconds} payload {Payload}", context.MessageId, messageTypeIdentifier, elapsed, Encoding.UTF8.GetString(context.Message.Body).MaxLines(10));
                    if (!verbose)
                        lock (SlowLock) SlowCommandTypes.Add(messageTypeIdentifier);
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
            factoryMethod: (b) => new TimeExecutionBehavior(b.Build<ILogger<TimeExecutionBehavior>>(), b.Build<Settings>())
        )
        {
            InsertBefore("MutateIncomingMessages");
        }
    }
}
