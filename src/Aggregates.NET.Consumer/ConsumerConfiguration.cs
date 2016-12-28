using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{

    public static class ConsumerConfiguration
    {
        public static void WithExtraStats(this ExposeSettings settings, bool use)
        {
            settings.GetSettings().Set("ExtraStats", use);
        }

        public static void MaxInFlight(this ExposeSettings settings, int inflight)
        {
            settings.GetSettings().Set("InFlight", inflight);
        }
    }
}
