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

        // Todo: make this queryable, extending query provider or something so whatever backend is storing snapshots can pass its queryable up to client
        IEnumerable<ISnapshot> Query<T, TId, TMemento>(String bucket, Expression<Func<TMemento, Boolean>> predicate) where T : class, IEntity where TMemento : class, IMemento<TId>;
    }
}
