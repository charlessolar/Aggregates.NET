using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Aggregates.Logging;

namespace Aggregates
{
    public static class StreamTypes
    {
        public static string Domain = "DOMAIN";
        public static string Delayed = "DELAY";
        public static string Snapshot = "SNAPSHOT";
        public static string Poco = "POCO";
        public static string OOB = "OOB";
    }

    public delegate string StreamIdGenerator(Type entityType, string streamType, string bucket, Id id, Id[] parents);

    public static class Defaults
    {
        public static readonly string PrefixHeader = "Aggregates.NET";
        public static readonly string MessageIdHeader = "MessageId";
        public static readonly string CorrelationIdHeader = "CorrelationId";

        public static readonly string OriginatingHeader = "Originating";

        public static readonly string Retries = "Aggregates.NET.Retries";
        public static readonly string RequestResponse = "Aggregates.NET.Request";
        public static readonly string ChannelKey = "Aggregates.NET.ChannelKey";
        public static readonly string OobHeaderKey = "Aggregates.OOB";
        public static readonly string OobTransientKey = "Aggregates.Transient";
        public static readonly string OobDaysToLiveKey = "Aggregates.DaysToLive";
        public static readonly string LocalHeader = "Aggregates.NET.LocalMessage";
        public static readonly string BulkHeader = "Aggregates.NET.BulkMessage";
        public static readonly string ConflictResolvedHeader = "ConflictResolver";

        public static readonly string AggregatesVersionHeader = "Aggregates.NET.Version";
        public static readonly string EndpointHeader = "Endpoint";
        public static readonly string InstanceHeader = "Endpoint.Instance";
        public static readonly string MachineHeader = "Endpoint.Machine";
        public static readonly string EndpointVersionHeader = "Endpoint.Version";


        public static Guid Instance = Guid.NewGuid();
        public static string Bucket = "default";
        public static string CommitIdHeader = "CommitId";

        public static AsyncLocal<LogLevel?> MinimumLogging = new AsyncLocal<LogLevel?>();

    }
}
