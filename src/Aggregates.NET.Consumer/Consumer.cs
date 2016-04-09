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
    public class DurableConsumer : Feature
    {
        public DurableConsumer()
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
            context.Container.ConfigureComponent<EventUnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<DurableSubscriber>(DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent(y =>
            {
                return new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                    ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                };
            }, DependencyLifecycle.SingleInstance);
        }
    }

    public class VolatileConsumer : Feature
    {
        public VolatileConsumer()
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
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<VolatileSubscriber>(DependencyLifecycle.SingleInstance);

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
    public class CompetingConsumer : Feature
    {
        public CompetingConsumer()
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
                s.SetDefault("HandledDomains", Int32.MaxValue);
                s.SetDefault("BucketHeartbeats", 5);
                s.SetDefault("BucketExpiration", 60);
                s.SetDefault("BucketCount", 1);
                s.SetDefault("BucketsHandled", 1);
                s.SetDefault("MaxQueueSize", 10000);
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<CompetingSubscriber>(DependencyLifecycle.SingleInstance);

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

    public class ConsumerRunner : FeatureStartupTask
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