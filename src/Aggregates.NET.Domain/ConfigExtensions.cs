using NServiceBus;
using NServiceBus.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class ConfigExtensions
    {
        public class Config
        {
            internal Config(BusConfiguration config)
            {
                Bus = config;
            }
            public readonly BusConfiguration Bus;
        }
        public static Config EventStoreDomain<TEventStore>(this BusConfiguration config) where TEventStore : NServiceBus.Features.Feature
        {
            config.EnableFeature<Aggregates.Feature>();
            config.EnableFeature<TEventStore>();
            return new Config(config);
        }
    }
}
