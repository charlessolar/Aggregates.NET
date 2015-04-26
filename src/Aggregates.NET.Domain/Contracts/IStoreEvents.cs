using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreEvents
    {
        ISnapshot GetSnapshot(String stream, Int32 version = StreamPosition.End);
        IEventStream GetStream(String stream, Int32 start = StreamPosition.Start);

        void WriteToStream(String stream, Int32 expectedVersion, IEnumerable<EventData> events);
    }
}