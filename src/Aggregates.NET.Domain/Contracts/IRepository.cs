using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepository : IDisposable
    {
        int TotalUncommitted { get; }
        int ChangedStreams { get; }

        // Checks stream versions in store if needed
        Task Prepare(Guid commitId);
        // Writes the stream
        Task Commit(Guid commitId, IDictionary<string, string> commitHeaders);
    }

    public interface IRepository<T> : IRepository where T : Entity<T>
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
        Task<T> New(string bucketId, Id id);
        Task<T> New(Id id);
    }
    public interface IRepository<TParent, T> : IRepository<T> where TParent : Entity<TParent> where T : Entity<T, TParent>
    {
    }
}