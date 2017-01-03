using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{

    public static class ConsumerConfiguration
    {
        public static void WithExtraStats(this ExposeSettings settings, bool use)
        {
            settings.GetSettings().Set("ExtraStats", use);
        }
        public static void WithParallelEvents(this ExposeSettings settings, int concurrency)
        {
            settings.GetSettings().Set("ParallelEvents", concurrency);
        }

    }
}
