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
using System.Threading;

namespace Aggregates
{
    public class ConsumerFeature : Aggregates.Feature
    {
        public ConsumerFeature() : base()
        {
            RegisterStartupTask<ConsumerRunner>();

            Defaults(s =>
            {
                s.SetDefault("Parallelism", Environment.ProcessorCount / 2);
                s.SetDefault("ParallelHandlers", true);
                s.SetDefault("ReadSize", 200);
                s.SetDefault("EventDropIsFatal", false);
                s.SetDefault("MaxQueueSize", 10000);
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.Container.ConfigureComponent<DefaultInvokeObjects>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<NServiceBusDispatcher>(DependencyLifecycle.SingleInstance);

            context.Pipeline.Register<FixSendIntentRegistration>();
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
            context.Container.ConfigureComponent<EventUnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
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
                s.SetDefault("BucketHeartbeats", 5);
                s.SetDefault("BucketExpiration", 20);
                s.SetDefault("BucketCount", 1);
                s.SetDefault("BucketsHandled", 1);
                s.SetDefault("PauseOnFreeBuckets", true);
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
        private Int32 _retryCount;
        private DateTime? _lastFailure;

        public ConsumerRunner(IBuilder builder, ReadOnlySettings settings, Configure configure)
        {
            _builder = builder;
            _settings = settings;
            _configure = configure;
        }

        private TimeSpan CalculateSleep()
        {
            if (_lastFailure.HasValue)
            {
                var lastSleep = (1 << _retryCount);
                if ((DateTime.UtcNow - _lastFailure.Value).TotalSeconds > (lastSleep * 5))
                    _retryCount = 0;
            }
            _retryCount++;
            _lastFailure = DateTime.UtcNow;
            // 8 seconds minimum sleep
            return TimeSpan.FromSeconds(1 << ((_retryCount / 2) + 2));
        }

        protected override void OnStart()
        {
            Logger.Debug("Starting event consumer");
            var subscriber = _builder.Build<IEventSubscriber>();
            subscriber.SubscribeToAll(_settings.EndpointName());
            subscriber.Dropped = (reason, ex) =>
            {
                Thread.Sleep(CalculateSleep());
                subscriber.SubscribeToAll(_settings.EndpointName());
            };
        }
    }
}