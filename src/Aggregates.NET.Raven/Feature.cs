using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.Raven
{
    public class Feature : NServiceBus.Features.Feature
    {
        public Feature()
        {
            RegisterStartupTask<RavenRunner>();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
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

        private class RavenRunner : FeatureStartupTask
        {
            private readonly IBuilder _builder;
            private readonly ReadOnlySettings _settings;
            private readonly Configure _configure;

            public RavenRunner(IBuilder builder, ReadOnlySettings settings, Configure configure)
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
