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
    public class DurableSubscriber : IEventSubscriber
    {
        private readonly IEventStoreConnection _client;
        private readonly IPersistCheckpoints _store;
        private readonly JsonSerializerSettings _settings;

        public DurableSubscriber(IEventStoreConnection client, IPersistCheckpoints store, JsonSerializerSettings settings)
        {
            _client = client;
            _store = store;
            _settings = settings;
        }

        public void SubscribeToAll(String endpoint, IDispatcher dispatcher)
        {
            var saved = _store.Load(endpoint);

            _client.SubscribeToAllFrom(saved, false, (_, e) =>
            {
                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                dispatcher.Dispatch(data);

                if (e.OriginalPosition.HasValue)
                    _store.Save(endpoint, e.OriginalPosition.Value);
            });
        }
    }
}