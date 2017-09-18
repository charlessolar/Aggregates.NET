using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Messages;

namespace Aggregates
{
    public interface IResolveConflicts
    {
        /// <summary>
        /// Entity is a clean entity without the Uncommitted events
        /// </summary>
        Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new();
    }
}
