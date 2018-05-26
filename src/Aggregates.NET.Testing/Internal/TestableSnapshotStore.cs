using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class TestableSnapshotStore : IStoreSnapshots
    {
        private Dictionary<string, ISnapshot> _snapshots;
        private Dictionary<string, IState> _writtenSnapshots;

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

        public Task<ISnapshot> GetSnapshot<T>(string bucket, Id streamId, Id[] parents) where T : IEntity
        {
            var key = $"{bucket}.{typeof(T).FullName}.{streamId}";
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
