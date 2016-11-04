using System;
using System.Threading;
using System.Threading.Tasks;
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
        public GetEventStore()
        {
            Defaults(s =>
            {
                s.SetDefault("EventStoreDelayed", false);
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {

            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StorePocos>(DependencyLifecycle.InstancePerCall);

            bool useDelayed;
            if (context.Settings.TryGet<bool>("EventStoreDelayed", out useDelayed) && useDelayed)
                context.Container.ConfigureComponent<DelayedChannel>(DependencyLifecycle.InstancePerCall);
        }

    }

}