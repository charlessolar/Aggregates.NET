using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{

    public static class ConsumerConfiguration
    {
        public static void SetBucketHeartbeats(this ExposeSettings settings, Int32 Seconds)
        {
            settings.GetSettings().Set("BucketHeartbeats", Seconds);
        }
        public static void SetBucketExpiration(this ExposeSettings settings, Int32 Seconds)
        {
            settings.GetSettings().Set("BucketExpiration", Seconds);
        }
        public static void SetBucketCount(this ExposeSettings settings, Int32 Count)
        {
            settings.GetSettings().Set("BucketCount", Count);
        }
        public static void SetBucketsHandled(this ExposeSettings settings, Int32 Count)
        {
            settings.GetSettings().Set("BucketsHandled", Count);
        }
        /// <summary>
        /// When true, application will not consume events unless ALL buckets are claimed
        /// Recommended unless you don't care about ordering
        /// </summary>
        public static void PauseOnFreeBuckets(this ExposeSettings settings, Boolean Pause)
        {
            settings.GetSettings().Set("PauseOnFreeBuckets", Pause);
        }
    }
}
