using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreEntities
    {
        Task<TEntity> New<TEntity, TState>(string bucket, Id id, Id[] parents) where TEntity : IEntity<TState> where TState : class, IState, new();
        Task<TEntity> Get<TEntity, TState>(string bucket, Id id, Id[] parents) where TEntity : IEntity<TState> where TState : class, IState, new();
        Task Verify<TEntity, TState>(TEntity entity) where TEntity : IEntity<TState> where TState : class, IState, new();
        Task Commit<TEntity, TState>(TEntity entity, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : class, IState, new();
    }
}
