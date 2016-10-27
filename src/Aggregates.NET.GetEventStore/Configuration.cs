using System;
using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{
    public static class Configuration
    {
        public static void UseEventStoreDelayedChannel(this ExposeSettings settings, bool use)
        {
            settings.GetSettings().Set("EventStoreDelayed", use);
        }
        
    }
}
