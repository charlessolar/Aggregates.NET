using Aggregates.Contracts;
using Aggregates.Integrations;
using Aggregates.Internal;
using NEventStore;
using NEventStore.Dispatcher;
using NServiceBus;
using NServiceBus.Logging;
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

        public static void UseAggregates(this BusConfiguration config, Wireup eventstore)
        {
            config.RegisterComponents(x => x.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork));
            config.RegisterComponents(x => x.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall));

            config.RegisterComponents(x => x.ConfigureComponent<IStoreEvents>(y =>
            {
                Logger.Debug("Configuring the store to dispatch messages synchronously.");

                // Dispatcher is a proxy class which uses IEventPublisher to get the current UnitOfWork for publishing events to the bus
                eventstore.UsingSynchronousDispatchScheduler(
                    new Dispatcher(y.Build<IBus>())
                    );

                return eventstore.Build();

            }, DependencyLifecycle.SingleInstance));
        }
    }
}
