using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class TestableOobWriter : IOobWriter
    {
        public Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, string oobId, long? start = null, int? count = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, string oobId, long? start = null, int? count = null) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task<long> GetSize<TEntity>(string bucket, Id streamId, Id[] parents, string oobId) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }

        public Task WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, Guid CommitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity
        {
            throw new NotImplementedException();
        }
    }
}
