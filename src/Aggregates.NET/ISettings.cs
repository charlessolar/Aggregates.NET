using Aggregates.Contracts;
using System;

namespace Aggregates
{
    public interface ISettings
    {
        IConfiguration Configuration { get; }
        Version EndpointVersion { get; }
        Version AggregatesVersion { get; }

        // Log settings
        TimeSpan? SlowAlertThreshold { get; }
        bool ExtraStats { get; }

        // Data settings
        StreamIdGenerator Generator { get; }
        int ReadSize { get; }
        Compression Compression { get; }

        // Messaging settings
        string Endpoint { get; }
        string UniqueAddress { get; }
        int Retries { get; }

        TimeSpan SagaTimeout { get; }
        bool AllEvents { get; }
        bool TrackChildren { get; }

        // Disable certain "production" features related to versioning 
        bool DevelopmentMode { get; }

        string CommandDestination { get; }

        string MessageContentType { get; }
    }
}
