using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{

    public static class ConsumerConfiguration
    {
        public static void SetBucketHeartbeats(this ExposeSettings settings, int seconds)
        {
            settings.GetSettings().Set("BucketHeartbeats", seconds);
        }
        public static void SetBucketExpiration(this ExposeSettings settings, int seconds)
        {
            settings.GetSettings().Set("BucketExpiration", seconds);
        }
        public static void SetBucketCount(this ExposeSettings settings, int count)
        {
            settings.GetSettings().Set("BucketCount", count);
        }
        public static void SetBucketsHandled(this ExposeSettings settings, int count)
        {
            settings.GetSettings().Set("BucketsHandled", count);
        }
        /// <summary>
        /// When true, application will not consume events unless ALL buckets are claimed
        /// Recommended unless you don't care about ordering
        /// </summary>
        public static void PauseOnFreeBuckets(this ExposeSettings settings, bool pause)
        {
            settings.GetSettings().Set("PauseOnFreeBuckets", pause);
        }
    }
}
