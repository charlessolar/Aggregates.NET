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
        private readonly static HashSet<String> _levelZero = new HashSet<string>();
        private static Int32 _stage = 0;
        private static Timer _unachableEviction = new Timer((_) =>
        {
            // Clear uncachable every 10 minutes
            if (_stage == 120)
            {
                _stage = 0;
                Logger.Write(LogLevel.Debug, () => $"Clearing {_uncachable.Count} uncachable stream names");
                _uncachable.Clear();
            }
            // Clear levelOne every minute
            if (_stage % 12 == 0)
                _levelOne.Clear();

            // Clear levelZero every 5 seconds
            _levelZero.Clear();

            _stage++;
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        private readonly Boolean _intelligent;

        public MemoryStreamCache(/*Boolean Intelligent = false*/)
        {
            _intelligent = false;
        }

        public void Cache(String stream, object cached)
        {
            if (_intelligent && (_uncachable.Contains(stream) || _levelOne.Contains(stream)))
                return;

            _cache.Set(stream, cached, new CacheItemPolicy { AbsoluteExpiration = DateTime.UtcNow + TimeSpan.FromMinutes(5) });
        }
        public void Evict(String stream)
        {
            if (_intelligent)
            {
                if (_uncachable.Contains(stream)) return;

                if (_levelZero.Contains(stream))
                {
                    if (_levelOne.Contains(stream))
                    {
                        Logger.Write(LogLevel.Info, () => $"Stream {stream} has been evicted frequenty, marking uncachable for a few minutes");
                        _uncachable.Add(stream);
                    }
                    else
                        _levelOne.Add(stream);
                }
                else
                    _levelZero.Add(stream);

            }            
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
