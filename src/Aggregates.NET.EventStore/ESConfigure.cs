using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ESConfigure
    {
        public static Configure EventStore(this Configure config, params IEventStoreConnection[] connections)
        {
            config.RegistrationTasks.Add((c) =>
            {
                var container = c.Container;

                container.Register<IEventStoreConsumer>((factory) =>
                    new EventStoreConsumer(
                        factory.Resolve<IMetrics>(),
                        factory.Resolve<IMessageSerializer>(),
                        factory.Resolve<IVersionRegistrar>(),
                        connections,
                        factory.Resolve<IEventMapper>()
                        ), Lifestyle.Singleton);
                container.Register<IStoreEvents>((factory) =>
                    new StoreEvents(
                        factory.Resolve<IMetrics>(),
                        factory.Resolve<IMessageSerializer>(),
                        factory.Resolve<IEventMapper>(),
                        factory.Resolve<IVersionRegistrar>(),
                        connections
                        ), Lifestyle.Singleton);

                return Task.CompletedTask;
            });

            // These tasks are needed for any endpoint connected to the eventstore
            // Todo: when implementing another eventstore, dont copy this, do it a better way
            config.StartupTasks.Add(async (c) =>
            {
                var subscribers = c.Container.ResolveAll<IEventSubscriber>();

                await subscribers.WhenAllAsync(x => x.Setup(
                    c.Endpoint,
                    Assembly.GetEntryAssembly().GetName().Version)
                ).ConfigureAwait(false);

                await subscribers.WhenAllAsync(x => x.Connect()).ConfigureAwait(false);

                // Only setup children projection if client wants it
                if (c.TrackChildren)
                {
                    var tracker = c.Container.Resolve<ITrackChildren>();
                    await tracker.Setup(c.Endpoint, Assembly.GetEntryAssembly().GetName().Version).ConfigureAwait(false);
                }

            });
            config.ShutdownTasks.Add(async (c) =>
            {
                var subscribers = c.Container.ResolveAll<IEventSubscriber>();

                await subscribers.WhenAllAsync(x => x.Shutdown()).ConfigureAwait(false);
            });

            return config;
        }
    }
}
