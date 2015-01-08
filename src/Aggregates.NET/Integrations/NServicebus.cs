using Aggregates.Contracts;
using Aggregates.Integrations;
using Aggregates.Internal;
using Aggregates.Messages;
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

        public static void UseAggregates(this BusConfiguration config, Func<IBuilder, IStoreEvents> eventStoreBuilder)
        {

            config.RegisterComponents(x => {
                x.ConfigureComponent<ExceptionFilter>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
                x.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<Dispatcher>(DependencyLifecycle.InstancePerCall);

                x.ConfigureComponent<IStoreEvents>(y => eventStoreBuilder(y), DependencyLifecycle.SingleInstance);
            });

            config.Pipeline.Register<ExceptionFilterRegistration>();    
        }
    }
}
