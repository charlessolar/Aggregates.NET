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
                s.SetDefault("HandlerParallelism", Environment.ProcessorCount);
                s.SetDefault("ProcessingParallelism", 1);
                s.SetDefault("ParallelHandlers", true); 
                s.SetDefault("ReadSize", 200);
                s.SetDefault("MaxRetries", -1);
                s.SetDefault("EventDropIsFatal", false);
                s.SetDefault("MaxQueueSize", 10000);
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
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