using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class StoreSnapshots : IStoreSnapshots
    {
        private readonly ILogger Logger;

        private readonly IStoreEvents _store;
        private readonly IMessageSerializer _serializer;
        private readonly IVersionRegistrar _registrar;

        public StoreSnapshots(ILogger<StoreSnapshots> logger, IStoreEvents store, IMessageSerializer serializer, IVersionRegistrar registrar)
        {
            Logger = logger;
            _store = store;
            _serializer = serializer;
            _registrar = registrar;
        }

        public async Task<ISnapshot> GetSnapshot<TEntity, TState>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity<TState> where TState : class, IState, new()
        {
            var snapshot = await _store.GetSnapshot<TEntity>(bucket, streamId, parents).ConfigureAwait(false);
            if (snapshot?.Payload is IState)
            {
                // Make a copy of the snapshot for use by user
                (snapshot.Payload as IState).Snapshot = _serializer.Deserialize<TState>(_serializer.Serialize(snapshot.Payload));
            }
            return snapshot;
        }


        public Task WriteSnapshots<T>(IState state, IDictionary<string, string> commitHeaders) where T : IEntity
        {
            // We don't need snapshots to store the previous snapshot
            // ideally this field would be [JsonIgnore] but we have no dependency on json.net
            state.Snapshot = null;
            var snapshot = new Snapshot
            {
                StreamId = state.Id,
                Bucket = state.Bucket,
                EntityType = _registrar.GetVersionedName(typeof(T)),
                Timestamp = DateTime.UtcNow,
                Version = state.Version + 1,
                Parents = state.Parents,
                Payload = state
            };
            Logger.DebugEvent("WriteSnapshot", "Writing snapshot for [{Stream:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", snapshot.StreamId, snapshot.Bucket, snapshot.EntityType, snapshot.Version);
            return _store.WriteSnapshot<T>(snapshot, commitHeaders);
        }

    }
}
