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
    public class ConsumerFeature : NServiceBus.Features.Feature
    {
        public ConsumerFeature()
        {
            Defaults(s =>
            {
                s.SetDefault("ExtraStats", false);
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask(builder => new EventStoreRunner(builder.Build<IEventSubscriber>(), context.Settings));

            context.Container.ConfigureComponent<EventSubscriber>(DependencyLifecycle.SingleInstance);
            
            context.Pipeline.Register(
                behavior: typeof(MutateIncomingEvents),
                description: "Running event mutators for incoming messages"
                );
            context.Pipeline.Register(
                behavior: typeof(EventUnitOfWork),
                description: "Begins and Ends event unit of work"
                );
        }
    }
    internal class EventStoreRunner : FeatureStartupTask, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStoreRunner));
        private readonly ReadOnlySettings _settings;
        private readonly IEventSubscriber _subscriber;
        private int _retryCount;
        private DateTime? _lastFailure;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public EventStoreRunner(IEventSubscriber subscriber, ReadOnlySettings settings)
        {
            _subscriber = subscriber;
            _settings = settings;
            _cancellationTokenSource = new CancellationTokenSource();
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
            Logger.Write(LogLevel.Info, "Starting event consumer");
            await _subscriber.Setup(
                _settings.EndpointName(),
                _settings.Get<int>("ReadSize"),
                _settings.Get<bool>("ExtraStats")).ConfigureAwait(false);


            await _subscriber.Subscribe(_cancellationTokenSource.Token).ConfigureAwait(false);
            _subscriber.Dropped = (reason, ex) =>
            {
                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    Logger.Write(LogLevel.Info, () => $"Event consumer stopped - cancelation requested");
                    return;
                }

                Logger.Warn($"Event consumer stopped due to exception: {ex.Message}.  Will restart", ex);
                Thread.Sleep(CalculateSleep());
                _subscriber.Subscribe(_cancellationTokenSource.Token).Wait();
            };
        }

        protected override Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Stopping event consumer");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _subscriber.Dispose();
        }
    }


}