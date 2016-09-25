using Aggregates.Contracts;
using NServiceBus.Logging;
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
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MemoryStreamCache));
        private readonly static MemoryCache _cache = new MemoryCache("Aggregates Cache", new System.Collections.Specialized.NameValueCollection { { "CacheMemoryLimitMegabytes", "500" } });
        
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

        public Boolean Update(String stream, object payload)
        {
            var cached = _cache.Get(stream);
            if (cached == null) return false;

            if(cached is IEventStream && payload is IWritableEvent)
            {
                Logger.DebugFormat("Updating cached stream [{0}]", stream);
                var real = (cached as IEventStream);
                Cache(stream, real.Clone(payload as IWritableEvent));
                return true;
            }
            return false;
        }
    }
}
