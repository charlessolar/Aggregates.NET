using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Features;

namespace Aggregates
{
    public class GetEventStore : NServiceBus.Features.Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StorePocos>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DomainSubscriber>(DependencyLifecycle.SingleInstance);
            
        }
        
    }
}