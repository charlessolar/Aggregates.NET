using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    public interface IRepository<T> : IRepository where T : class, IEventSource
    {
        /// <summary>
        /// Attempts to get aggregate from store, if stream does not exist it throws
        /// </summary>
        /// <typeparam name="TId"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Get<TId>(TId id);
        Task<T> Get<TId>(string bucketId, TId id);
        /// <summary>
        /// Attempts to retreive aggregate from store, if stream does not exist it does not throw
        /// </summary>
        /// <typeparam name="TId"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> TryGet<TId>(TId id);
        Task<T> TryGet<TId>(string bucketId, TId id);

        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        /// <typeparam name="TId"></typeparam>
        /// <param name="bucketId"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> New<TId>(string bucketId, TId id);
        Task<T> New<TId>(TId id);
    }
}