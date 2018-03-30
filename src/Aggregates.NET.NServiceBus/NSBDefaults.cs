using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public static class NSBDefaults
    {
        // Header information to take from incoming messages
        public static IList<string> CarryOverHeaders = new List<string>
        {
            "NServiceBus.MessageId",
            "NServiceBus.CorrelationId",
            "NServiceBus.Version",
            "NServiceBus.TimeSent",
            "NServiceBus.ConversationId",
            "NServiceBus.OriginatingMachine",
            "NServiceBus.OriginatingEndpoint"
        };
        public static string MessageIdHeader = "NServiceBus.MessageId";
        public static string CorrelationIdHeader = "NServiceBus.CorrelationId";
    }
}
