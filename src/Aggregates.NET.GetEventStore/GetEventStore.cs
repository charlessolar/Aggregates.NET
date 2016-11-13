using System;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using NServiceBus.Transport;
using NServiceBus.Unicast;

namespace Aggregates
{
    public class GetEventStore : NServiceBus.Features.Feature
    {

        protected override void Setup(FeatureConfigurationContext context)
        {
            var settings = context.Settings;

            // NSB's DI is not quite there yet to support a per unit of work instance of EventStoreDelayed that will call UnitOfWork.Start and End
            context.Container.ConfigureComponent(b => (IDelayedChannel)new EventStoreDelayed(b.Build<IStoreEvents>()), DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent(b => (IApplicationUnitOfWork)b.Build<IDelayedChannel>(), DependencyLifecycle.InstancePerUnitOfWork);

            context.Container.ConfigureComponent(b => 
                new StoreEvents(b.Build<IEventStoreConnection>(), b.Build<IMessageMapper>(), settings.Get<int>("ReadSize"), settings.Get<bool>("Compress")), 
                DependencyLifecycle.InstancePerCall);
        }

    }

}