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
            "NServiceBus.ConversationId",
            "NServiceBus.OriginatingMachine",
            "NServiceBus.OriginatingEndpoint"
        };
        public static readonly string MessageIdHeader = "NServiceBus.MessageId";
        public static readonly string CorrelationIdHeader = "NServiceBus.CorrelationId";
    }
}
