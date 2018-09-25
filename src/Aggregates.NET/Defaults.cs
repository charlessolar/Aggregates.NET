using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Aggregates.Logging;

namespace Aggregates
{
    public static class StreamTypes
    {
        public const string Domain = "DOMAIN";
        public const string Delayed = "DELAY";
        public const string Snapshot = "SNAPSHOT";
        public const string OOB = "OOB";
    }

    public delegate string StreamIdGenerator(Type entityType, string streamType, string bucket, Id id, Id[] parents);

    public static class Defaults
    {
        public const string PrefixHeader = "Aggregates.NET";
        public const string MessageIdHeader = "MessageId";
        public const string CorrelationIdHeader = "CorrelationId";

        public const string OriginatingHeader = "Originating";

        public const string MessageVersionHeader = "Aggregates.NET.Version";
        public const string Retries = "Aggregates.NET.Retries";
        public const string RequestResponse = "Aggregates.NET.Request";
        public const string ChannelKey = "Aggregates.NET.ChannelKey";
        public const string OobHeaderKey = "Aggregates.OOB";
        public const string OobTransientKey = "Aggregates.Transient";
        public const string OobDaysToLiveKey = "Aggregates.DaysToLive";
        public const string LocalHeader = "Aggregates.NET.LocalMessage";
        public const string BulkHeader = "Aggregates.NET.BulkMessage";
        public const string ConflictResolvedHeader = "ConflictResolver";

        public const string AggregatesVersionHeader = "Aggregates.NET.LibraryVersion";
        public const string EndpointHeader = "Endpoint";
        public const string InstanceHeader = "Endpoint.Instance";
        public const string MachineHeader = "Endpoint.Machine";
        public const string EndpointVersionHeader = "Endpoint.Version";
        
        public const string Bucket = "default";
        public const string CommitIdHeader = "CommitId";

        public static readonly Guid Instance = Guid.NewGuid();
        public static readonly AsyncLocal<LogLevel?> MinimumLogging = new AsyncLocal<LogLevel?>();

    }
}
