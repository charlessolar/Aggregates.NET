using Aggregates.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MemoryStreamCache : IStreamCache
    {
        private readonly static ConcurrentDictionary<String, IEventStream> _cache =
            new ConcurrentDictionary<string, IEventStream>();
        
        public void Cache(String stream, IEventStream eventstream)
        {
            _cache.TryAdd(stream, eventstream);
        }
        public void Evict(String stream)
        {
            IEventStream eventstream;
            _cache.TryRemove(stream, out eventstream);
        }
        public IEventStream Retreive(String stream)
        {
            IEventStream eventstream = null;
            _cache.TryGetValue(stream, out eventstream);
            if (eventstream == null) return null;

            return eventstream.Clone();
        }
    }
}
