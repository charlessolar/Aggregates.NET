using System;
using EventStore.ClientAPI;
using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{
    public static class Configuration
    {
        /// <summary>
        /// Sets the interval to flush delayed messages when using IDelayedChannel
        /// If you don't want to write every delayed event to ES each UOW set this
        /// * Should be set lowish - depending on your load
        /// </summary>
        public static void DelayedFlushInterval(this ExposeSettings settings, TimeSpan interval)
        {
            settings.GetSettings().Set("FlushInterval", interval);
        }
        /// <summary>
        /// Delayed message expiration
        /// When messages are delayed for bulk processing, only let them sit for this amount of time before flushing to eventstore for someone else to process
        /// </summary>
        public static void DelayedExpiration(this ExposeSettings settings, TimeSpan expiration)
        {
            settings.GetSettings().Set("DelayedExpiration", expiration);
        }

        public static void ShardedStore(this ExposeSettings settings, IEventStoreConnection[] connections)
        {
            settings.GetSettings().Set("Shards", connections);
        }
        public static void WithExtraStats(this ExposeSettings settings, bool use)
        {
            settings.GetSettings().Set("ExtraStats", use);
        }

    }
}
