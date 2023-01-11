using Aggregates.Contracts;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class TestableSnapshotStore : IStoreSnapshots
    {
        private readonly Dictionary<string, ISnapshot> _snapshots;
        private readonly Dictionary<string, IState> _writtenSnapshots;

        public TestableSnapshotStore()
        {
            _snapshots = new Dictionary<string, ISnapshot>();
            _writtenSnapshots = new Dictionary<string, IState>();
        }

        public void SpecifySnapshot<T>(string bucket, Id streamId, object payload)
        {
            _snapshots[$"{bucket}.{typeof(T).FullName}.{streamId}"] = new Internal.Snapshot
            {
                Bucket = bucket,
                EntityType = typeof(T).FullName,
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

        public Task WriteSnapshots<T>(IState memento, IDictionary<string, string> commitHeaders) where T : IEntity
        {
            var key = $"{memento.Bucket}.{typeof(T).FullName}.{memento.Id}";
            _writtenSnapshots[key] = memento;

            return Task.CompletedTask;
        }
    }
}
