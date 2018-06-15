using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class TestableEventStore : IStoreEvents
    {
        private Dictionary<string, IFullEvent[]> _events = new Dictionary<string, IFullEvent[]>();


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


        public Task<IFullEvent[]> GetEvents(string stream, long? start = null, int? count = null)
        {
            throw new NotImplementedException();
        }

        public Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            // ignore start and count, not needed for tests
            var key = $"{typeof(TEntity).FullName}.{bucket}.{streamId}.{parents.BuildParentsString()}";
            if (!_events.ContainsKey(key))
                throw new NotFoundException();
            return Task.FromResult(_events[key]);
        }

        public Task<IFullEvent[]> GetEventsBackwards(string stream, long? start = null, int? count = null)
        {
            throw new NotImplementedException();
        }

        public Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            var key = $"{typeof(TEntity).FullName}.{bucket}.{streamId}.{parents.BuildParentsString()}";
            if (!_events.ContainsKey(key))
                throw new ArgumentException("undefined stream");
            return Task.FromResult(_events[key].Reverse().ToArray());
        }

        public Task<string> GetMetadata<TEntity>(string bucket, Id streamId, Id[] parents, string key) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<string> GetMetadata(string stream, string key)
        {
            throw new NotImplementedException();
        }

        public Task<long> Size<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<long> Size(string stream)
        {
            throw new NotImplementedException();
        }

        public Task<bool> VerifyVersion(string stream, long expectedVersion)
        {
            throw new NotImplementedException();
        }

        public Task<bool> VerifyVersion<TEntity>(string bucket, Id streamId, Id[] parents, long expectedVersion) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<long> WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<long> WriteEvents(string stream, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null)
        {
            throw new NotImplementedException();
        }

        public Task WriteMetadata(string stream, long? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null, bool force = false, IDictionary<string, string> custom = null)
        {
            throw new NotImplementedException();
        }

        public Task WriteMetadata<TEntity>(string bucket, Id streamId, Id[] parents, long? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null, bool force = false, IDictionary<string, string> custom = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }
    }
}
