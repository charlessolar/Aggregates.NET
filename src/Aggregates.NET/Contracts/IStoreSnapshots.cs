using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        Task<ISnapshot> GetSnapshot<TEntity, TState>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity<TState> where TState : class, IState, new();
        Task WriteSnapshots<T>(IState memento, IDictionary<string, string> commitHeaders) where T : IEntity;
    }
}
