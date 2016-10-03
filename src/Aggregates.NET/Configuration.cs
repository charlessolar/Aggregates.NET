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
        public static void ImmediateRetries(this ExposeSettings settings, Int32 Max)
        {
            settings.GetSettings().Set("ImmediateRetries", Max);
        }
        public static void DelayedRetries(this ExposeSettings settings, Int32 Max)
        {
            settings.GetSettings().Set("DelayedRetries", Max);
        }
        public static void RetryForever(this ExposeSettings settings, Boolean Forever)
        {
            settings.GetSettings().Set("RetryForever", Forever);
        }
        public static void SlowAlertThreshold(this ExposeSettings settings, Int32 Milliseconds)
        {
            settings.GetSettings().Set("SlowAlertThreshold", Milliseconds);
        }
        public static void SetReadSize(this ExposeSettings settings, Int32 Count)
        {
            settings.GetSettings().Set("ReadSize", Count);
        }
        /// <summary>
        /// Compress events and messages using GZip
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="Compress"></param>
        public static void SetCompress(this ExposeSettings settings, Boolean Compress)
        {
            settings.GetSettings().Set("Compress", Compress);
        }
        public static void ConfigureForAggregates(this RecoverabilitySettings recoverability)
        {
            var settings = recoverability.GetSettings();
            settings.Set(Defaults.SETUP_CORRECTLY, true);
            
            // Set immediate retries to our "MaxRetries" setting
            recoverability.Immediate(x =>
            {
                x.NumberOfRetries(settings.Get<Int32>("ImmediateRetries"));
            });
            recoverability.Delayed(x =>
            {
                x.TimeIncrease(TimeSpan.FromSeconds(2));
                if (settings.Get<Boolean>("RetryForever"))
                    x.NumberOfRetries(Int32.MaxValue);
                else
                    x.NumberOfRetries(settings.Get<Int32>("DelayedRetries"));
            });
        }
    }
}
