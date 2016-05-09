using NServiceBus.Configuration.AdvanceExtensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class Configuration
    {
        public static void MaxEventRetries(this ExposeSettings settings, Int32 Max)
        {
            settings.GetSettings().Set("MaxRetries", Max);
        }
        public static void SlowAlertThreshold(this ExposeSettings settings, Int32 Milliseconds)
        {
            settings.GetSettings().Set("SlowAlertThreshold", Milliseconds);
        }
    }
}
