using System;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.MessageInterfaces;
using Aggregates.Internal;

namespace Aggregates.GetEventStore
{
    public class Feature : ConsumerFeature
    {
        public Feature()
        {
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DomainSubscriber>(DependencyLifecycle.SingleInstance);
            
        }
        
    }
}