using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
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
                s.SetDefault("ParallelEvents", 10);
            });
            DependsOn<Aggregates.Feature>();
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask(builder => new EventStoreRunner(builder.BuildAll<IEventSubscriber>(), context.Settings));

            var settings = context.Settings;
            context.Container.ConfigureComponent(b =>
            {
                var concurrency = settings.Get<int>("ParallelEvents");

                return new EventSubscriber(b.Build<MessageHandlerRegistry>(), b.Build<IMessageMapper>(), b.Build<MessageMetadataRegistry>(), b.Build<IEventStoreConsumer>(), concurrency);
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent(b =>
            {
                var compress = settings.Get<Compression>("Compress");
                return new SnapshotReader(b.Build<IStoreEvents>(), b.Build<IMessageMapper>(), b.Build<IEventStoreConsumer>(), compress);
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent(b =>
            {
                return new DelayedSubscriber(b.Build<IEventStoreConsumer>(), settings.Get<int>("Retries"));
            }, DependencyLifecycle.SingleInstance);

            context.Pipeline.Register<MutateIncomingEventRegistration>();

            // bulk invoke only possible with consumer feature because it uses the eventstore as a sink when overloaded
            context.Pipeline.Replace("InvokeHandlers", (b) =>
                new BulkInvokeHandlerTerminator(b.Build<IMessageMapper>()),
                "Replaces default invoke handlers with one that supports our custom delayed invoker");
        }
    }
    internal class EventStoreRunner : FeatureStartupTask, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("EventStoreRunner");
        private readonly ReadOnlySettings _settings;
        private readonly IEnumerable<IEventSubscriber> _subscribers;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public EventStoreRunner(IEnumerable<IEventSubscriber> subscribers, ReadOnlySettings settings)
        {
            _subscribers = subscribers;
            _settings = settings;
            _cancellationTokenSource = new CancellationTokenSource();
        }
        
        protected override async Task OnStart(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Starting event consumer");
            await _subscribers.WhenAllAsync(x => x.Setup(
                    _settings.EndpointName(),
                    _cancellationTokenSource.Token)
            ).ConfigureAwait(false);

            await _subscribers.WhenAllAsync(x => x.Connect()).ConfigureAwait(false);
        }

        protected override Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Stopping event consumer");
            _cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach(var subscriber in _subscribers)
                subscriber.Dispose();
        }
    }


}