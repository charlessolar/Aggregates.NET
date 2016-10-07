using NServiceBus.Configuration.AdvanceExtensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public delegate String StreamIdGenerator(Type entityType, String bucket, String id);

    public static class Configuration
    {

        public static void ShouldCacheEntities(this ExposeSettings settings, Boolean cache)
        {
            settings.GetSettings().Set("ShouldCacheEntities", cache);
        }
        /// <summary>
        /// When we have a version conflict with the store we can try to resolve it automatically.  This sets how many times we'll try
        /// Set to 0 to disable
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="tries"></param>
        public static void MaxConflictResolves(this ExposeSettings settings, Int32 tries)
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
        public static void SetWatchConflicts(this ExposeSettings settings, Boolean Watch)
        {
            settings.GetSettings().Set("WatchConflicts", Watch);
        }
        public static void SetClaimThreshold(this ExposeSettings settings, Int32 Threshold)
        {
            settings.GetSettings().Set("ClaimThreshold", Threshold);
        }
        public static void SetExpireConflict(this ExposeSettings settings, TimeSpan Length)
        {
            settings.GetSettings().Set("ExpireConflict", Length);
        }
        public static void SetClaimLength(this ExposeSettings settings, TimeSpan Length)
        {
            settings.GetSettings().Set("ClaimLength", Length);
        }
    }
}
