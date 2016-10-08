using System;
using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{
    public delegate string StreamIdGenerator(Type entityType, string bucket, string id);

    public static class Configuration
    {

        public static void ShouldCacheEntities(this ExposeSettings settings, bool cache)
        {
            settings.GetSettings().Set("ShouldCacheEntities", cache);
        }
        /// <summary>
        /// When we have a version conflict with the store we can try to resolve it automatically.  This sets how many times we'll try
        /// Set to 0 to disable
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="tries"></param>
        public static void MaxConflictResolves(this ExposeSettings settings, int tries)
        {
            settings.GetSettings().Set("MaxConflictResolves", tries);
        }
        /// <summary>
        /// Sets how to generate stream ids throughout
        /// Parameters:
        /// - Type of entity
        /// - bucket
        /// - id
        /// Returns:
        /// - stream id
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="generator"></param>
        public static void SetStreamGenerator(this ExposeSettings settings, StreamIdGenerator generator)
        {
            settings.GetSettings().Set("StreamGenerator", generator);
        }
        public static void SetWatchConflicts(this ExposeSettings settings, bool watch)
        {
            settings.GetSettings().Set("WatchConflicts", watch);
        }
        public static void SetClaimThreshold(this ExposeSettings settings, int threshold)
        {
            settings.GetSettings().Set("ClaimThreshold", threshold);
        }
        public static void SetExpireConflict(this ExposeSettings settings, TimeSpan length)
        {
            settings.GetSettings().Set("ExpireConflict", length);
        }
        public static void SetClaimLength(this ExposeSettings settings, TimeSpan length)
        {
            settings.GetSettings().Set("ClaimLength", length);
        }
    }
}
