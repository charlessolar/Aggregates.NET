using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreEvents
    {
        IEventStream GetStream<T>(String bucket, String stream, Int32? start = null) where T : class, IEntity;

        void WriteEvents(String bucket, String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders);
    }
}