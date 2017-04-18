using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Contracts
{
    public interface ISnapshotReader
    {
        Task Connect(string endpoint, CancellationToken cancelToken);
        
        Task<ISnapshot> Retreive(string stream);
    }
}
