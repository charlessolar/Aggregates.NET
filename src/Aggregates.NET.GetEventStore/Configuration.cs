using System;
using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{
    public static class Configuration
    {
        /// <summary>
        /// Sets the interval to flush delayed messages when using IDelayedChannel
        /// If you don't want to write every delayed event to ES each UOW set this
        /// * Should be set lowish - depending on your load
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="interval"></param>
        public static void DelayedFlushInterval(this ExposeSettings settings, TimeSpan? interval)
        {
            settings.GetSettings().Set("FlushInterval", interval);
        }
    }
}
