using NServiceBus;
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
        public static void MaxRetries(this ExposeSettings settings, Int32 Max)
        {
            settings.GetSettings().Set("MaxRetries", Max);
        }
        public static void SlowAlertThreshold(this ExposeSettings settings, Int32 Milliseconds)
        {
            settings.GetSettings().Set("SlowAlertThreshold", Milliseconds);
        }
        public static void SetReadSize(this ExposeSettings settings, Int32 Count)
        {
            settings.GetSettings().Set("ReadSize", Count);
        }
        public static void ConfigureForAggregates(this RecoverabilitySettings recoverability)
        {
            var settings = recoverability.GetSettings();
            settings.Set(Defaults.SETUP_CORRECTLY, true);
            
            // Set immediate retries to our "MaxRetries" setting
            recoverability.Immediate(x =>
            {
                x.NumberOfRetries(settings.Get<Int32>("MaxRetries"));
            });
            // Disable delayed retries 
            recoverability.Delayed(x =>
            {
                x.NumberOfRetries(0);
            });
        }
    }
}
