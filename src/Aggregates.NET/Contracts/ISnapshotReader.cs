using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Contracts
{
    public interface ISnapshotReader : IDisposable
    {
        Task Setup(string endpoint);

        Task Subscribe(CancellationToken cancelToken);

        Task<IWritableEvent> Retreive(string stream);
    }
}
