using System;
using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{

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

        public static void PublishOobToBus(this ExposeSettings settings, bool useOobNsb)
        {
            settings.GetSettings().Set("UseNsbForOob", true);
        }
    }
}
