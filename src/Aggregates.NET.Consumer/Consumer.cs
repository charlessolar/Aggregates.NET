using System;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates
{
    public class ConsumerFeature : Feature
    {
        public ConsumerFeature()
        {
            Defaults(s =>
            {
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            
            context.Pipeline.Register(
                behavior: typeof(MutateIncomingEvents),
                description: "Running event mutators for incoming messages"
                );
            context.Pipeline.Register(
                behavior: new EventUnitOfWork(context.Settings.Get<int>("SlowAlertThreshold")),
                description: "Begins and Ends event unit of work"
                );
        }
    }

    public class DurableConsumer : ConsumerFeature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.RegisterStartupTask(builder => new ConsumerRunner(builder, context.Settings));

            context.Container.ConfigureComponent<Checkpointer>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DurableSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }

    public class VolatileConsumer : ConsumerFeature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            context.RegisterStartupTask(builder => new ConsumerRunner(builder, context.Settings));

            context.Container.ConfigureComponent<VolatileSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }
    public class CompetingConsumer : ConsumerFeature
    {
        public CompetingConsumer()
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
            context.RegisterStartupTask(builder => new ConsumerRunner(builder, context.Settings));

            context.Container.ConfigureComponent<CompetingSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }

    public class ConsumerRunner : FeatureStartupTask
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ConsumerRunner));
        private readonly IBuilder _builder;
        private readonly ReadOnlySettings _settings;
        private int _retryCount;
        private DateTime? _lastFailure;

        public ConsumerRunner(IBuilder builder, ReadOnlySettings settings)
        {
            _builder = builder;
            _settings = settings;
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

        protected override async Task OnStart(IMessageSession session)
        {
            Logger.Write(LogLevel.Debug, "Starting event consumer");
            var subscriber = _builder.Build<IEventSubscriber>();
            subscriber.SubscribeToAll(session, _settings.EndpointName());
            subscriber.Dropped = (reason, ex) =>
            {
                Thread.Sleep(CalculateSleep());
                subscriber.SubscribeToAll(session, _settings.EndpointName());
            };

            await session.Publish<IConsumerAlive>(x =>
            {
                x.Endpoint = _settings.InstanceSpecificQueue();
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

        }
        protected override async Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Debug, "Stopping event consumer");
            await session.Publish<IConsumerDead>(x =>
            {
                x.Endpoint = _settings.InstanceSpecificQueue();
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);
        }
    }
}