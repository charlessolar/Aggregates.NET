using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class NullStorePocos : IStorePocos
    {
        public Task<Tuple<long, T>> Get<T>(string bucket, Id streamId, Id[] parents) where T : class
        {
            throw new NotImplementedException();
        }

        public Task Write<T>(Tuple<long, T> poco, string bucket, Id streamId, Id[] parents, IDictionary<string, string> commitHeaders)
        {
            throw new NotImplementedException();
        }
    }
}
