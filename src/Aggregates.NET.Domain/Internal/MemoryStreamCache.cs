using Aggregates.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MemoryStreamCache : IStreamCache
    {
        private readonly static MemoryCache _cache = new MemoryCache("Aggregates Cache");
        
        public void Cache(String stream, IEventStream eventstream)
        {
            _cache.Set(stream, eventstream, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(5) });
        }
        public void Evict(String stream)
        {
            _cache.Remove(stream);
        }
        public IEventStream Retreive(String stream)
        {
            var eventstream = _cache.Get(stream) as IEventStream;
            if (eventstream == null) return null;

            return eventstream.Clone();
        }
        public void CacheSnap(String stream, ISnapshot snapshot)
        {
            _cache.Set(stream, snapshot, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(5) });
        }
        public void EvictSnap(String stream)
        {
            _cache.Remove(stream);
        }
        public ISnapshot RetreiveSnap(String stream)
        {
            var snap = _cache.Get(stream) as ISnapshot;

            return snap;
        }
    }
}
