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

namespace Aggregates
{
    public class EventStore : Feature
    {
        public EventStore()
        {
            RegisterStartupTask<EventStoreRunner>();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<GetEventStore>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<JsonSerializerSettings>(y =>
                {
                    return new JsonSerializerSettings
                    {
                        Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                        ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>()),
                        TypeNameHandling = TypeNameHandling.All
                    };
                }, DependencyLifecycle.SingleInstance);
        }

        private class EventStoreRunner : FeatureStartupTask
        {
            private readonly IBuilder _builder;
            private readonly ReadOnlySettings _settings;
            private readonly Configure _configure;

            public EventStoreRunner(IBuilder builder, ReadOnlySettings settings, Configure configure)
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