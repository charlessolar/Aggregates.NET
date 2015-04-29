using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class GetEventStore
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(GetEventStore));
        public static void UseGetEventStore(this BusConfiguration config, IEventStoreConnection client)
        {
            config.RegisterComponents(x =>
            {
                x.ConfigureComponent<IEventStoreConnection>((y) =>
                {
                    // Setup the persistant store subscription to send events out NSB
                    client.DispatchEvents(y.Build<IDispatcher>(), y.Build<JsonSerializerSettings>());
                    return client;
                }, DependencyLifecycle.SingleInstance);
                x.ConfigureComponent<EventStore>(DependencyLifecycle.InstancePerUnitOfWork);

                x.ConfigureComponent<JsonSerializerSettings>(y =>
                {
                    return new JsonSerializerSettings
                    {
                        Binder = new EventSerializationBinder(y.Build<IMessageMapper>()),
                        ContractResolver = new EventContractResolver(y.Build<IMessageMapper>(), y.Build<IMessageCreator>())
                    };
                }, DependencyLifecycle.SingleInstance);
            });
        }
    }
}