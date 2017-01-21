using System;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Settings;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;

namespace Aggregates
{
    public class ConsumerFeature : NServiceBus.Features.Feature
    {
        public ConsumerFeature()
        {
            Defaults(s =>
            {
                s.SetDefault("ExtraStats", false);
                s.SetDefault("ParallelEvents", 4);
            });
            DependsOn<Aggregates.Feature>();
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask(builder => new EventStoreRunner(builder.Build<IEventSubscriber>(), context.Settings));

            var settings = context.Settings;
            context.Container.ConfigureComponent(b =>
            {
                IEventStoreConnection[] connections;
                if (!settings.TryGet<IEventStoreConnection[]>("Shards", out connections))
                    connections = new[] { b.Build<IEventStoreConnection>() };
                var concurrency = settings.Get<int>("ParallelEvents");

                return new EventSubscriber(b.Build<MessageHandlerRegistry>(), b.Build<IMessageMapper>(), b.Build<MessageMetadataRegistry>(), connections, concurrency);
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent(b =>
            {
                IEventStoreConnection[] connections;
                if (!settings.TryGet<IEventStoreConnection[]>("Shards", out connections))
                    connections = new[] { b.Build<IEventStoreConnection>() };
                var compress = settings.Get<Compression>("Compress");
                return new SnapshotReader(b.Build<IStoreEvents>(), b.Build<IMessageMapper>(), connections, compress);
            }, DependencyLifecycle.SingleInstance);

            context.Pipeline.Register<MutateIncomingEventRegistration>();
        }
    }
    internal class EventStoreRunner : FeatureStartupTask, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("EventStoreRunner");
        private readonly ReadOnlySettings _settings;
        private readonly IEventSubscriber _subscriber;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public EventStoreRunner(IEventSubscriber subscriber, ReadOnlySettings settings)
        {
            _subscriber = subscriber;
            _settings = settings;
            _cancellationTokenSource = new CancellationTokenSource();
        }
        
        protected override async Task OnStart(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Starting event consumer");
            await _subscriber.Setup(
                _settings.EndpointName(),
                _settings.Get<int>("ReadSize"),
                _settings.Get<bool>("ExtraStats")).ConfigureAwait(false);

            await _subscriber.Subscribe(_cancellationTokenSource.Token).ConfigureAwait(false);
        }

        protected override Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Stopping event consumer");
            _cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _subscriber.Dispose();
        }
    }


}