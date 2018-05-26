using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepository : IDisposable
    {
    }

    internal interface IRepositoryCommit
    {
        int ChangedStreams { get; }

        // Checks stream versions in store if needed
        Task Prepare(Guid commitId);
        // Writes the stream
        Task Commit(Guid commitId, IDictionary<string, string> commitHeaders);
    }

    public interface IRepository<TEntity> : IRepository where TEntity : IEntity
    {
        /// <summary>
        /// Attempts to get aggregate from store, if stream does not exist it throws
        /// </summary>
        Task<TEntity> Get(Id id);
        Task<TEntity> Get(string bucket, Id id);
        /// <summary>
        /// Attempts to retreive aggregate from store, if stream does not exist it does not throw
        /// </summary>
        Task<TEntity> TryGet(Id id);
        Task<TEntity> TryGet(string bucket, Id id);

        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        Task<TEntity> New(Id id);
        Task<TEntity> New(string bucket, Id id);
    }
    public interface IRepository<TEntity, TParent> : IRepository where TParent : IEntity where TEntity : IChildEntity<TParent>
    {
        /// <summary>
        /// Attempts to get aggregate from store, if stream does not exist it throws
        /// </summary>
        Task<TEntity> Get(Id id);
        /// <summary>
        /// Attempts to retreive aggregate from store, if stream does not exist it does not throw
        /// </summary>
        Task<TEntity> TryGet(Id id);
        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        Task<TEntity> New(Id id);
    }
}