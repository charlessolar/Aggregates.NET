using System;
using System.Collections.Generic;
using System.Threading;
using NServiceBus.Logging;

namespace Aggregates
{
    public static class Defaults
    {
        public static readonly string SetupCorrectly = "Aggregates.NET.Safe";
        public static readonly string Retries = "Aggregates.NET.Retries";
        public static readonly string RequestResponse = "Aggregates.NET.Request";

        public static Guid Instance = Guid.NewGuid();
        public static string Bucket = "default";
        public static string MessageIdHeader = "Originating.NServiceBus.MessageId";
        public static string CommitIdHeader = "CommitId";
        public static string InstanceHeader = "Instance";
        
        public static AsyncLocal<LogLevel?> MinimumLogging = new AsyncLocal<LogLevel?>();

        // Header information to take from incoming messages
        public static IList<string> CarryOverHeaders = new List<string> {
                                                                          "NServiceBus.MessageId",
                                                                          "NServiceBus.CorrelationId",
                                                                          "NServiceBus.Version",
                                                                          "NServiceBus.TimeSent",
                                                                          "NServiceBus.ConversationId",
                                                                          "NServiceBus.OriginatingMachine",
                                                                          "NServiceBus.OriginatingEndpoint"
                                                                      };
    }
}
