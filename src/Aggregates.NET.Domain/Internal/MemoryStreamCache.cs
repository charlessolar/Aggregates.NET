using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MemoryStreamCache : IStreamCache
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MemoryStreamCache));
        private readonly static MemoryCache _cache = new MemoryCache("Aggregates Cache", new System.Collections.Specialized.NameValueCollection { { "CacheMemoryLimitMegabytes", "500" } });
        // For streams that are changing multiple times a second, no sense to cache them if they get immediately evicted
        private readonly static HashSet<String> _uncachable = new HashSet<string>();
        private readonly static HashSet<String> _levelOne = new HashSet<string>();
        private static String _lastEvicted = "";
        private static Timer _unachableEviction = new Timer((_) =>
        {
            Logger.Write(LogLevel.Debug, () => $"Clearing {_uncachable.Count} uncachable stream names");
            _uncachable.Clear();
        }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        public void Cache(String stream, object cached)
        {
            if (_uncachable.Contains(stream) || _levelOne.Contains(stream))
                return;

            _cache.Set(stream, cached, new CacheItemPolicy { AbsoluteExpiration = DateTime.UtcNow + TimeSpan.FromMinutes(5) });
        }
        public void Evict(String stream)
        {
            // Logic here:
            // If a stream is getting evicted multiple times in a row, it would mean there are a lot of commands being processed that are changing the same stream
            // What this will do is whenever a stream is evicted it will be first saved as the "lastEvicted"
            // Next eviction if its the same stream, then it moves the stream to levelOne, kind of a buffer zone that will prevent the stream
            // from being cached, but is also very temporary.  If the next eviction is not the same stream, then the previous stream is removed 
            // from level one.
            // If it IS the same stream for a third time, then its moved into the _uncachable set where it won't be cached for as much as 10 minutes
            // depending on the timer.
            // So tldr, if a stream is evicted 3 times in a row its marked "uncachable" for up to 10 minutes
            if (stream.Equals(_lastEvicted))
            {
                if (_levelOne.Contains(stream))
                {
                    Logger.Write(LogLevel.Info, () => $"Stream {stream} has been evicted 3 times in a row, marking uncachable for a few minutes");
                    _uncachable.Add(stream);
                }
                else
                    _levelOne.Add(stream);
            }
            else
                _levelOne.Remove(_lastEvicted);


            _lastEvicted = stream;
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

            if (cached is IEventStream && payload is IWritableEvent)
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
