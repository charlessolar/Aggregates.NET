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
using NServiceBus.Pipeline;

namespace Aggregates
{
    public class ConsumerFeature : Feature
    {
        public ConsumerFeature() : base()
        {
            RegisterStartupTask<ConsumerRunner>();

            Defaults(s =>
            {
                s.SetDefault("Parallelism", Environment.ProcessorCount / 2);
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
            context.Container.ConfigureComponent<DefaultInvokeObjects>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent(y =>
            {
                return new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                    ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                };
            }, DependencyLifecycle.SingleInstance);


            context.Pipeline.Replace(WellKnownStep.LoadHandlers, typeof(AsyncronizedLoad), "Loads the message handlers");
            context.Pipeline.Replace(WellKnownStep.InvokeHandlers, typeof(AsyncronizedInvoke), "Invokes the message handler with Task.Run");
            context.Pipeline.Register<SafetyNetRegistration>();

            MessageScanner.Scan(context);
        }
    }

    public class DurableConsumer : ConsumerFeature
    {
        public DurableConsumer() : base()
        {
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.Container.ConfigureComponent<DurableSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }

    public class VolatileConsumer : ConsumerFeature
    {
        public VolatileConsumer() : base()
        {
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.Container.ConfigureComponent<VolatileSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }
    public class CompetingConsumer : ConsumerFeature
    {
        public CompetingConsumer() : base()
        {
            Defaults(s =>
            {
                s.SetDefault("HandledDomains", Int32.MaxValue);
                s.SetDefault("BucketHeartbeats", 5);
                s.SetDefault("BucketExpiration", 60);
                s.SetDefault("BucketCount", 1);
                s.SetDefault("BucketsHandled", 1);
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.Container.ConfigureComponent<CompetingSubscriber>(DependencyLifecycle.SingleInstance);
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

    public static class MessageScanner
    {
        public static void Scan(FeatureConfigurationContext context)
        {

            foreach (var handler in context.Settings.GetAvailableTypes().Where(IsAsyncMessage))
                context.Container.ConfigureComponent(handler, DependencyLifecycle.InstancePerCall);
        }
        private static bool IsAsyncMessage(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
            {
                return false;
            }

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleMessagesAsync<>));
        }
    }
}