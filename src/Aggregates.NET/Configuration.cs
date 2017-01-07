using System;
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
            throw new InvalidOperationException("Compress is not supported yet");
            settings.GetSettings().Set("Compress", compress);
        }
        public static void ConfigureForAggregates(this RecoverabilitySettings recoverability, int immediateRetries = 10)
        {
            var settings = recoverability.GetSettings();

            settings.Set(Defaults.SetupCorrectly, true);
            settings.Set("Retries", immediateRetries);

            // Set immediate retries to our "MaxRetries" setting
            recoverability.Immediate(x =>
            {
                x.NumberOfRetries(immediateRetries);
            });
            
            recoverability.Delayed(x =>
            {
                // Delayed retries don't work well with the InMemory context bag storage.  Creating
                // a problem of possible duplicate commits
                x.NumberOfRetries(0);
                //x.TimeIncrease(TimeSpan.FromSeconds(1));
                //x.NumberOfRetries(forever ? int.MaxValue : delayedRetries);
            });
        }
    }
}
