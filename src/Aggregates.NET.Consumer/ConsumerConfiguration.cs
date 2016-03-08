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
        public static void SetEventStoreMaxDegreeOfParallelism( this ExposeSettings settings, Int32 Parallelism)
        {
            settings.GetSettings().Set("SetEventStoreMaxDegreeOfParallelism", Parallelism);
        }
        public static void SetEventStoreCapacity(this ExposeSettings settings, Int32 Capacity)
        {
            settings.GetSettings().Set("SetEventStoreCapacity", Capacity);
        }
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
        public static void SetReadSize(this ExposeSettings settings, Int32 Count)
        {
            settings.GetSettings().Set("ReadSize", Count);
        }
    }
}
