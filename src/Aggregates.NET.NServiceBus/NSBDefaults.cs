using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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

        public const string AggregatesSettings = "Aggregates.NET.Settings";
    }
}
