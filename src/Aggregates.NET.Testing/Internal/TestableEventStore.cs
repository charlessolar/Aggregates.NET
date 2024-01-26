using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class TestableEventStore : IStoreEvents
    {
        private readonly Dictionary<string, IFullEvent[]> _events = new Dictionary<string, IFullEvent[]>();


        public bool StreamExists<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            var key = $"{typeof(TEntity).FullName}.{bucket}.{streamId}.{parents.BuildParentsString()}";
            return _events.ContainsKey(key);
        }

        public void AddEvent<TEntity>(string bucket, Id streamId, Id[] parents, Messages.IEvent @event) where TEntity : IEntity
        {
            var key = $"{typeof(TEntity).FullName}.{bucket}.{streamId}.{parents.BuildParentsString()}";
            var fullEvent =
                    new FullEvent
                    {
                        Descriptor = new EventDescriptor(),
                        Event = @event,
                        EventId = Guid.NewGuid()
                    };

            if (!_events.ContainsKey(key))
                _events[key] = new[] { fullEvent };
            else
                _events[key] = _events[key].Concat(new[] { fullEvent }).ToArray();
        }
        // create the test stream with no events so its "found" by event reader but not hydrated
        public void Exists<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            var key = $"{typeof(TEntity).FullName}.{bucket}.{streamId}.{parents.BuildParentsString()}";
            if (_events.ContainsKey(key))
                return;
            _events[key] = new IFullEvent[] { };
        }


        public Task<IFullEvent[]> GetEvents<TEntity>(StreamDirection direction, string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            // ignore start and count, not needed for tests
            var key = $"{typeof(TEntity).FullName}.{bucket}.{streamId}.{parents.BuildParentsString()}";
            if (!_events.ContainsKey(key))
                throw new NotFoundException();

            var ret = _events[key].AsEnumerable();
            if (direction == StreamDirection.Backwards)
                ret = ret.Reverse();
            return Task.FromResult(ret.ToArray());
        }

        [ExcludeFromCodeCoverage]
        public Task<ISnapshot> GetSnapshot<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }
        [ExcludeFromCodeCoverage]
        public Task WriteSnapshot<TEntity>(ISnapshot snapshot, IDictionary<string, string> commitHeaders) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }



        [ExcludeFromCodeCoverage]
        public Task<bool> VerifyVersion<TEntity>(string bucket, Id streamId, Id[] parents, long expectedVersion) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        [ExcludeFromCodeCoverage]
        public Task<long> WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }


        [ExcludeFromCodeCoverage]
        public Task WriteMetadata<TEntity>(string bucket, Id streamId, Id[] parents, int? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }
    }
}
