using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;

namespace Aggregates
{
    public class VolatileSubscriber : IEventSubscriber
    {
        private readonly IEventStoreConnection _client;
        private readonly JsonSerializerSettings _settings;

        public VolatileSubscriber(IEventStoreConnection client, JsonSerializerSettings settings)
        {
            _client = client;
            _settings = settings;
        }

        public void SubscribeToAll(String endpoint, IDispatcher dispatcher)
        {
            _client.SubscribeToAllFrom(Position.End, false, (_, e) =>
            {
                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                dispatcher.Dispatch(data);
            });
        }
    }
}