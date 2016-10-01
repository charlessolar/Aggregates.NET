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
        /// <summary>
        /// Set the number of message retries
        /// A setting of -1 will effectively make the app retry forever (Int32.MaxValue)
        /// The actual number of delayed retries is always 3 with 2 second time increases
        /// The number of immediate retries in between delays is Max / 3
        /// See: https://docs.particular.net/nservicebus/recoverability/?version=Core_6
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="Max"></param>
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
                var max = settings.Get<Int32>("MaxRetries");
                if (max == -1)
                    x.NumberOfRetries(Int32.MaxValue);
                else
                    x.NumberOfRetries( max / 3 );
            });
            // Disable delayed retries 
            recoverability.Delayed(x =>
            {
                x.NumberOfRetries(3);
                x.TimeIncrease(TimeSpan.FromSeconds(2));
            });
        }
    }
}
