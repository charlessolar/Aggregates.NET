using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class NSBDefaults
    {
        // Header information to take from incoming messages
        public static readonly IReadOnlyCollection<string> CarryOverHeaders = new string[]
        {
            "NServiceBus.MessageId",
            "NServiceBus.CorrelationId",
            "NServiceBus.Version",
            "NServiceBus.TimeSent",
            /* "NServiceBus.ConversationId", */ // NSB has issues setting this header
            "NServiceBus.OriginatingMachine",
            "NServiceBus.OriginatingEndpoint"
        };
        public const string MessageIdHeader = "NServiceBus.MessageId";
        public const string CorrelationIdHeader = "NServiceBus.CorrelationId";
        public const string RetryHeader = "NServiceBus.Retries";
        public const string FailedHeader = "NServiceBus.TimeOfFailure";
        public const string ExceptionTypeHeader = "NServiceBus.ExceptionInfo.ExceptionType";
        public const string ExceptionMessageHeader = "NServiceBus.ExceptionInfo.Message";
        public const string ExceptionStack = "NServiceBus.ExceptionInfo.StackTrace";

        public const string AggregatesSettings = "Aggregates.NET.Settings";
        public const string AggregatesConfiguration = "Aggregates.NET.Configuration";
    }
}
