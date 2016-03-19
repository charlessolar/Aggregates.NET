using System;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.MessageInterfaces;
using Aggregates.Internal;

namespace Aggregates.GetEventStore
{
    public class Feature : NServiceBus.Features.Feature
    {
        public Feature()
        {
            RegisterStartupTask<ConsumerRunner>();

            Defaults(s =>
            {
                s.SetDefault("SetEventStoreMaxDegreeOfParallelism", Environment.ProcessorCount);
                s.SetDefault("ParallelHandlers", true); 
                s.SetDefault("ReadSize", 500);
                s.SetDefault("MaxRetries", 5);
                s.SetDefault("EventDropIsFatal", false);
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<EventUnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<DomainSubscriber>(DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent(y =>
                {
                    return new JsonSerializerSettings
                    {
                        Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                        ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                    };
                }, DependencyLifecycle.SingleInstance);
        }
        
    }
}