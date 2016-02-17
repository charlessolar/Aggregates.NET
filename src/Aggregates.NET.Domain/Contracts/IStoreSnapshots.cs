using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        ISnapshot GetSnapshot(String bucket, String stream);
        void WriteSnapshots(String bucket, String stream, IEnumerable<ISnapshot> snapshots);
    }
}
