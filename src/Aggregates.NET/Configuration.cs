using System;
using Metrics.Sampling;
using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;

namespace Aggregates
{
    public static class Configuration
    {
        public static void SlowAlertThreshold(this ExposeSettings settings, int milliseconds)
        {
            settings.GetSettings().Set("SlowAlertThreshold", milliseconds);
        }

        public static void SetReadSize(this ExposeSettings settings, int count)
        {
            settings.GetSettings().Set("ReadSize", count);
        }

        public static void EnableSlowAlerts(this ExposeSettings settings, bool expose)
        {
            settings.GetSettings().Set("SlowAlerts", expose);
        }

    /// <summary>
        /// Compress events and messages using GZip
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="compress"></param>
        public static void SetCompress(this ExposeSettings settings, bool compress)
        {
            settings.GetSettings().Set("Compress", compress);
        }
        public static void ConfigureForAggregates(this RecoverabilitySettings recoverability, int immediateRetries = 12, int delayedRetries = 3, bool forever = false)
        {
            var settings = recoverability.GetSettings();

            settings.Set(Defaults.SetupCorrectly, true);
            settings.Set("ImmediateRetries", immediateRetries);
            settings.Set("DelayedRetries", delayedRetries);
            settings.Set("RetryForever", forever);

            // Set immediate retries to our "MaxRetries" setting
            recoverability.Immediate(x =>
            {
                x.NumberOfRetries(immediateRetries);
            });
            recoverability.Delayed(x =>
            {
                x.TimeIncrease(TimeSpan.FromSeconds(2));
                x.NumberOfRetries(forever ? int.MaxValue : delayedRetries);
            });
        }
    }
}
