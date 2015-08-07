using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface ISnapshotting
    {
        void RestoreSnapshot(Object snapshot);
        Object TakeSnapshot();
        Boolean ShouldTakeSnapshot();
    }
}