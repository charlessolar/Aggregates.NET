using Aggregates.Contracts;
using Aggregates.Integrations;
using Aggregates.Internal;
using NEventStore;
using NEventStore.Dispatcher;
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

    public static class NServicebus
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServicebus));

        public static void UseAggregates(this BusConfiguration config)
        {
            config.RegisterComponents(x => {
                x.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
                x.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<Dispatcher>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<EventContractResolver>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<EventSerializationBinder>(DependencyLifecycle.InstancePerCall);
            });
            
        }

        public static void ConnectEventStore( this BusConfiguration config, Func<IBuilder, IStoreEvents> builder )
        {
            config.RegisterComponents(x =>
            {
                x.ConfigureComponent<IStoreEvents>(y => builder(y), DependencyLifecycle.SingleInstance);
            });
        }
    }
}
