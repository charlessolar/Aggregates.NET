using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class NullStoreSnapshots : IStoreSnapshots
    {
        public Task<ISnapshot> GetSnapshot<T>(string bucket, Id streamId, Id[] parents) where T : IEntity
        {
            throw new NotImplementedException();
        }

        public Task WriteSnapshots<T>(string bucket, Id streamId, Id[] parents, long version, IState memento, IDictionary<string, string> commitHeaders) where T : IEntity
        {
            throw new NotImplementedException();
        }
    }
}
