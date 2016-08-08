using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    // Will store streams in a cache until they are updated by a new event
    public interface IStreamCache
    {
        //void Cache(String stream, IEventStream eventstream);
        //IEventStream Retreive(String stream);

        //void CacheSnap(String stream, ISnapshot snapshot);
        //ISnapshot RetreiveSnap(String stream);

        void Cache(String stream, object cached);
        object Retreive(String stream);

        void Evict(String stream);
    }
}
