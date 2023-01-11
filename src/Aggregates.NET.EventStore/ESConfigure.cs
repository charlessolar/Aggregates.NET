using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ESConfigure
    {
        [ExcludeFromCodeCoverage]
        public class ESSettings
        {
            internal Dictionary<string, EventStoreClientSettings> _definedConnections = new Dictionary<string, EventStoreClientSettings>();

            public ESSettings AddClient(string connectionString, string name)
            {
                var settings = EventStoreClientSettings.Create(connectionString);
                return AddClient(settings, name);
            }
            public ESSettings AddClient(EventStoreClientSettings settings, string name)
            {
                if (_definedConnections.ContainsKey(name))
                    throw new ArgumentException($"Eventstore client [{name}] already defined");

                // need to do this because endpoint is not inside the eventstore data we have available
                _definedConnections[name] = settings;
                return this;
            }
        }
        public static Settings EventStore(this Settings config, Action<ESSettings> eventStoreConfig)
        {
            var esSettings = new ESSettings();
            eventStoreConfig(esSettings);

            if (!esSettings._definedConnections.Any())
                throw new ArgumentException("Atleast 1 eventstore client must be defined");

            // Prevents creation of event store connections until Provider is available
            // since logger needs ILogger defined
            Func<IServiceProvider, IEnumerable<EventStore.Client.EventStoreClient>> connections = (provider) => esSettings._definedConnections.Select(x =>
            {
                var logFactory = provider.GetRequiredService<ILoggerFactory>();
                x.Value.LoggerFactory = logFactory;
                x.Value.ConnectionName = $"{x.Key}.Streams";

                var _logger = logFactory.CreateLogger("EventStoreClient");
                _logger.InfoEvent("Connect", "Connecting to eventstore as [{Name}]", x.Value.ConnectionName);
                return new EventStore.Client.EventStoreClient(x.Value);
            }).ToArray();
            Func<IServiceProvider, IEnumerable<EventStore.Client.EventStoreProjectionManagementClient>> projectionConnections = (provider) => esSettings._definedConnections.Select(x =>
            {
                var logFactory = provider.GetRequiredService<ILoggerFactory>();
                x.Value.LoggerFactory = logFactory;
                x.Value.ConnectionName = $"{x.Key}.Projection";

                var _logger = logFactory.CreateLogger("EventStoreClient");
                _logger.InfoEvent("Connect", "Connecting to projection eventstore as [{Name}]", x.Value.ConnectionName);
                return new EventStoreProjectionManagementClient(x.Value);
            });
            Func<IServiceProvider, IEnumerable<EventStore.Client.EventStorePersistentSubscriptionsClient>> persistentSubConnections = (provider) => esSettings._definedConnections.Select(x =>
            {
                var logFactory = provider.GetRequiredService<ILoggerFactory>();
                x.Value.LoggerFactory = logFactory;
                x.Value.ConnectionName = $"{x.Key}.PersistSub";

                var _logger = logFactory.CreateLogger("EventStoreClient");
                _logger.InfoEvent("Connect", "Connecting to persistent subscription eventstore as [{Name}]", x.Value.ConnectionName);
                return new EventStorePersistentSubscriptionsClient(x.Value);
            });

            Settings.RegistrationTasks.Add((container, settings) =>
            {
                container.AddSingleton<IEnumerable<EventStore.Client.EventStoreClient>>(connections);
                container.AddSingleton<IEnumerable<EventStore.Client.EventStoreProjectionManagementClient>>(projectionConnections);
                container.AddSingleton<IEnumerable<EventStore.Client.EventStorePersistentSubscriptionsClient>>(persistentSubConnections);

                container.AddSingleton<IEventStoreClient, Internal.EventStoreClient>();
                container.AddTransient<IStoreEvents, StoreEvents>();
                container.AddTransient<IEventStoreConsumer, EventStoreConsumer>();


                return Task.CompletedTask;
            });

            // These tasks are needed for any endpoint connected to the eventstore
            // Todo: when implementing another eventstore, dont copy this, do it a better way
            Settings.StartupTasks.Add(async (provider, settings) =>
            {
                var subscriber = provider.GetRequiredService<IEventSubscriber>();
                var logFactory = provider.GetRequiredService<ILoggerFactory>();
                var _logger = logFactory.CreateLogger("EventStoreClient");

                try
                {
                    await subscriber.Setup(
                        settings.Endpoint,
                        settings.EndpointVersion)
                    .ConfigureAwait(false);

                    await subscriber.Connect().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.InfoEvent("StartupTask", "EventStore subscriber failed to connect", ex);
                    throw;
                }
                // Only setup children projection if client wants it
                if (settings.TrackChildren)
                {
                    var tracker = provider.GetService<ITrackChildren>();
                    // Use Aggregates.net version because its our children projection nothing to do with user code
                    await tracker.Setup(settings.Endpoint, settings.AggregatesVersion).ConfigureAwait(false);
                }

            });
            Settings.ShutdownTasks.Add(async (container, settings) =>
            {
                var subscriber = container.GetRequiredService<IEventSubscriber>();

                await subscriber.Shutdown();
            });

            return config;
        }
    }
}
