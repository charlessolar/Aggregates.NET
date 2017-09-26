using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepository : IDisposable
    {
        int ChangedStreams { get; }

        // Checks stream versions in store if needed
        Task Prepare(Guid commitId);
        // Writes the stream
        Task Commit(Guid commitId, IDictionary<string, string> commitHeaders);
    }

    public interface IRepository<T> : IRepository where T : IEntity
    {
        /// <summary>
        /// Attempts to get aggregate from store, if stream does not exist it throws
        /// </summary>
        Task<T> Get(Id id);
        Task<T> Get(string bucketId, Id id);
        /// <summary>
        /// Attempts to retreive aggregate from store, if stream does not exist it does not throw
        /// </summary>
        Task<T> TryGet(Id id);
        Task<T> TryGet(string bucketId, Id id);

        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        Task<T> New(Id id);
        Task<T> New(string bucketId, Id id);
    }
    public interface IRepository<T, TParent> : IRepository where TParent : IEntity where T : IChildEntity<TParent>
    {
        /// <summary>
        /// Attempts to get aggregate from store, if stream does not exist it throws
        /// </summary>
        Task<T> Get(Id id);
        /// <summary>
        /// Attempts to retreive aggregate from store, if stream does not exist it does not throw
        /// </summary>
        Task<T> TryGet(Id id);
        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        Task<T> New(Id id);
    }
}