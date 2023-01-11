using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace Aggregates
{
    public static class StreamTypes
    {
        public const string Domain = "DOMAIN";
        public const string Snapshot = "SNAPSHOT";
        public const string Children = "CHILDREN";
    }

    public delegate string StreamIdGenerator(string entityType, string streamType, string bucket, Id id, Id[] parents);

    public static class Defaults
    {
        public const string PrefixHeader = "Aggregates.NET";
        public const string MessageIdHeader = "MessageId";
        public const string CorrelationIdHeader = "CorrelationId";

        public const string OriginatingHeader = "Originating";

        public const string Retries = "Aggregates.NET.Retries";
        public const string RequestResponse = "Aggregates.NET.Request";
        public const string ChannelKey = "Aggregates.NET.ChannelKey";
        public const string LocalHeader = "Aggregates.NET.LocalMessage";
        public const string BulkHeader = "Aggregates.NET.BulkMessage";
        public const string SagaHeader = "Aggregates.NET.Saga";
        public const string OriginatingMessageHeader = "Aggregates.NET.OriginatingMessage";

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
