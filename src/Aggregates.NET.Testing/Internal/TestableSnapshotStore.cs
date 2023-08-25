using Aggregates.Contracts;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class TestableSnapshotStore : IStoreSnapshots
    {
        private readonly Dictionary<string, ISnapshot> _snapshots;
        private readonly Dictionary<string, IState> _writtenSnapshots;

        public TestableSnapshotStore()
        {
            _snapshots = new Dictionary<string, ISnapshot>();
            _writtenSnapshots = new Dictionary<string, IState>();
        }

        public void SpecifySnapshot<TEntity>(string bucket, Id streamId, object payload)
        {
            _snapshots[$"{bucket}.{typeof(TEntity).FullName}.{streamId}"] = new Internal.Snapshot
            {
                Bucket = bucket,
                EntityType = typeof(TEntity).FullName,
                StreamId = streamId,
                Payload = payload,
            };
        }

        public Task<ISnapshot> GetSnapshot<TEntity, TState>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity<TState> where TState : class, IState, new()
        {
            var key = $"{bucket}.{typeof(TEntity).FullName}.{streamId}";
            if (!_snapshots.ContainsKey(key))
                return Task.FromResult<ISnapshot>(null);
            return Task.FromResult(_snapshots[key]);
        }

        public Task WriteSnapshots<TEntity>(IState memento, IDictionary<string, string> commitHeaders) where TEntity : IEntity
        {
            var key = $"{memento.Bucket}.{typeof(TEntity).FullName}.{memento.Id}";
            _writtenSnapshots[key] = memento;

            return Task.CompletedTask;
        }
    }
}
