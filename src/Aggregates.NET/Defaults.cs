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
        public static readonly string PrefixHeader = "Originating";
        public static readonly string EventPrefixHeader = "Event";
        public static readonly string DelayedPrefixHeader = "Delayed";
        public static readonly string Retries = "Aggregates.NET.Retries";
        public static readonly string RequestResponse = "Aggregates.NET.Request";
        public static readonly string DelayedId = "Aggregates.NET.DelayedMessageId";
        public static readonly string ChannelKey = "Aggregates.NET.ChannelKey";
        public static readonly string OobHeaderKey = "Aggregates.OOB";
        public static readonly string OobTransientKey = "Aggregates.Transient";
        public static readonly string OobDaysToLiveKey = "Aggregates.DaysToLive";
        public static readonly string LocalHeader = "Aggregates.NET.LocalMessage";
        public static readonly string LocalBulkHeader = "Aggregates.NET.LocalBulkMessage";


        public static Guid Instance = Guid.NewGuid();
        public static string Bucket = "default";
        public static string CommitIdHeader = "CommitId";
        public static string InstanceHeader = "Instance";

        public static AsyncLocal<LogLevel?> MinimumLogging = new AsyncLocal<LogLevel?>();

    }
}
