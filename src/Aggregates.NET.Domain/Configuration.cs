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
    }
}
