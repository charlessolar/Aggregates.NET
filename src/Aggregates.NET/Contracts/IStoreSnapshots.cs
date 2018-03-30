using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        Task<ISnapshot> GetSnapshot<T>(string bucket, Id streamId, Id[] parents) where T : IEntity;
        Task WriteSnapshots<T>(IState memento, IDictionary<string, string> commitHeaders) where T : IEntity;
    }
}
