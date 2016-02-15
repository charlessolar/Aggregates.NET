using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class ConfigExtensions
    {

        public static void EventStoreConsumer<TConsumer>(this BusConfiguration config) where TConsumer : NServiceBus.Features.Feature
        {
            config.EnableFeature<Aggregates.GetEventStore.Feature>();
            config.EnableFeature<TConsumer>();
        }
    }
}
