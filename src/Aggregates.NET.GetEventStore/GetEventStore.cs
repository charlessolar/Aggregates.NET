using System;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.MessageInterfaces;
using Aggregates.Internal;

namespace Aggregates.GetEventStore
{
    public class GetEventStore : NServiceBus.Features.Feature
    {
        public GetEventStore()
        {
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask((builder) => new ConsumerRunner(builder, context.Settings));
            
            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StorePocos>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DomainSubscriber>(DependencyLifecycle.SingleInstance);
            
        }
        
    }
}