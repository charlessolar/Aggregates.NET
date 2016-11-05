using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{

    public static class ConsumerConfiguration
    {
        public static void UseEventStoreDelayedChannel(this ExposeSettings settings, bool use)
        {
            settings.GetSettings().Set("EventStoreDelayed", use);
        }
    }
}
