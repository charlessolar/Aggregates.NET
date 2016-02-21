using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using Aggregates.Internal;

namespace Aggregates
{
    public class DurableConsumer : NServiceBus.Features.Feature
    {
        public DurableConsumer()
        {
            RegisterStartupTask<ConsumerRunner>();
            DependsOn<NServiceBus.Features.Feature>();

            Defaults(s =>
            {
                s.SetDefault("SetEventStoreMaxDegreeOfParallelism", Environment.ProcessorCount);
                s.SetDefault("SetEventStoreCapacity", new Tuple<int, int>(1024, 1024));
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<DurableSubscriber>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Dispatcher>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<JsonSerializerSettings>(y =>
            {
                return new JsonSerializerSettings
                {
                    Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                    ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                };
            }, DependencyLifecycle.SingleInstance);
        }
    }

    public class VolatileConsumer : NServiceBus.Features.Feature
    {
        public VolatileConsumer()
        {
            RegisterStartupTask<ConsumerRunner>();
            DependsOn<NServiceBus.Features.Feature>();

            Defaults(s =>
            {
                s.SetDefault("SetEventStoreMaxDegreeOfParallelism", Environment.ProcessorCount);
                s.SetDefault("SetEventStoreCapacity", new Tuple<int, int>(64, 64));
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<VolatileSubscriber>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Dispatcher>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<JsonSerializerSettings>(y =>
            {
                return new JsonSerializerSettings
                {
                    Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                    ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                };
            }, DependencyLifecycle.SingleInstance);
        }
    }

    internal class ConsumerRunner : FeatureStartupTask
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ConsumerRunner));
        private readonly IBuilder _builder;
        private readonly ReadOnlySettings _settings;
        private readonly Configure _configure;

        public ConsumerRunner(IBuilder builder, ReadOnlySettings settings, Configure configure)
        {
            _builder = builder;
            _settings = settings;
            _configure = configure;
        }

        protected override void OnStart()
        {
            Logger.Debug("Starting event consumer");
            _builder.Build<IEventSubscriber>().SubscribeToAll(_settings.EndpointName());
        }
    }
}