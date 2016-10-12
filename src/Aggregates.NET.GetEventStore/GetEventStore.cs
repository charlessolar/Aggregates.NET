using System;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates
{
    public class GetEventStore : NServiceBus.Features.Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask(builder => new EventStoreRunner(builder, context.Settings));

            context.Container.ConfigureComponent<StoreEvents>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StoreSnapshots>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<StorePocos>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DomainSubscriber>(DependencyLifecycle.SingleInstance);
        }

    }

    internal class EventStoreRunner : FeatureStartupTask
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStoreRunner));
        private readonly IBuilder _builder;
        private readonly ReadOnlySettings _settings;
        private int _retryCount;
        private DateTime? _lastFailure;

        public EventStoreRunner(IBuilder builder, ReadOnlySettings settings)
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

        protected override Task OnStart(IMessageSession session)
        {
            Logger.Write(LogLevel.Debug, "Starting event consumer");
            var subscriber = _builder.Build<IEventSubscriber>();
            subscriber.SubscribeToAll(session, _settings.EndpointName());
            subscriber.Dropped = (reason, ex) =>
            {
                Thread.Sleep(CalculateSleep());
                subscriber.SubscribeToAll(session, _settings.EndpointName());
            };
            return Task.CompletedTask;
        }

        protected override Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Debug, "Stopping event consumer");
            return Task.CompletedTask;
        }
    }
}