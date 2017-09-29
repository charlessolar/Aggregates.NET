using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class NullStoreEvents : IStoreEvents
    {
        public Task<IFullEvent[]> GetEvents(string stream, long? start = null, int? count = null)
        {
            throw new NotImplementedException();
        }

        public Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<IFullEvent[]> GetEventsBackwards(string stream, long? start = null, int? count = null)
        {
            throw new NotImplementedException();
        }

        public Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
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
