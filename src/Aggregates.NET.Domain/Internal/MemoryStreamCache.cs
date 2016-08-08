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
        
        public void Cache(String stream, object cached)
        {
            _cache.Set(stream, cached, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(5) });
        }
        public void Evict(String stream)
        {
            _cache.Remove(stream);
        }
        public object Retreive(String stream)
        {
            var cached = _cache.Get(stream);
            if (cached == null) return null;

            return cached;
        }
    }
}
