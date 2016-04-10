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
        private readonly static MemoryCache _cache = new MemoryCache("EventStreams");
        
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
            IEventStream eventstream = null;
            _cache.Get(stream);
            if (eventstream == null) return null;

            return eventstream.Clone();
        }
    }
}
