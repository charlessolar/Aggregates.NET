using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreEvents
    {
        ISnapshot GetSnapshot<T>(String stream) where T : class, IEntity;
        IEventStream GetStream<T>(String stream, Int32? start = null) where T : class, IEntity;

        void WriteEvents(String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, Object> commitHeaders);
        void WriteSnapshots(String stream, IEnumerable<IWritableEvent> snapshots, IDictionary<String, Object> commitHeaders);
    }
}