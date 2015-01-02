using Aggregates.Integrations;
using NEventStore;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class NServicebusRavenDB
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServicebusRavenDB));

        public static void UseAggregatesWithRaven(this BusConfiguration config, Func<IBuilder, IStoreEvents> eventStoreBuilder)
        {
            config.UseAggregates(eventStoreBuilder);
            config.RegisterComponents(x =>
            {
                x.ConfigureComponent<EventContractResolver>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<EventSerializationBinder>(DependencyLifecycle.InstancePerCall);
            });

        }
    }
}
