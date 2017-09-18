using Aggregates.Contracts;
using Aggregates.Internal;
using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class ESConfigure
    {
        public static Configure EventStore(this Configure config, IEventStoreConnection[] connections)
        {
            config.SetupTasks.Add(() =>
            {
                var container = Configuration.Settings.Container;

                container.Register<IEventStoreConsumer>((factory) =>
                    new EventStoreConsumer(
                        factory.Resolve<IMetrics>(),
                        factory.Resolve<IMessageSerializer>(),
                        connections,
                        config.ReadSize,
                        config.ExtraStats
                        ));
                container.Register<IStoreEvents>((factory) =>
                    new StoreEvents(
                        factory.Resolve<IMetrics>(),
                        factory.Resolve<IMessageSerializer>(),
                        factory.Resolve<IEventMapper>(),
                        config.Generator,
                        config.ReadSize,
                        config.Compression,
                        connections
                        ));

                return Task.CompletedTask;
            });


            return config;
        }
    }
}
