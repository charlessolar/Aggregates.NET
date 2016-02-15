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

namespace Aggregates.GetEventStore
{
    public class Feature : NServiceBus.Features.Feature
    {
        public Feature()
        {
            RegisterStartupTask<Runner>();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            
            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<JsonSerializerSettings>(y =>
                {
                    return new JsonSerializerSettings
                    {
                        Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                        ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                    };
                }, DependencyLifecycle.SingleInstance);
        }

        private class Runner : FeatureStartupTask
        {
            private readonly IBuilder _builder;
            private readonly ReadOnlySettings _settings;
            private readonly Configure _configure;

            public Runner(IBuilder builder, ReadOnlySettings settings, Configure configure)
            {
                _builder = builder;
                _settings = settings;
                _configure = configure;
            }

            protected override void OnStart()
            {
            }
        }
    }
}