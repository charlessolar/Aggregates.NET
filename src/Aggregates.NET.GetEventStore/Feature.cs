using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
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