using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Caching;
using System.Threading;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    public class MemoryStreamCache : IStreamCache, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MemoryStreamCache));
        private static readonly MemoryCache MemCache = new MemoryCache("Aggregates Cache", new NameValueCollection { { "CacheMemoryLimitMegabytes", "500" } });
        // For streams that are changing multiple times a second, no sense to cache them if they get immediately evicted
        private static readonly HashSet<string> Uncachable = new HashSet<string>();
        private static readonly HashSet<string> LevelOne = new HashSet<string>();
        private static readonly HashSet<string> LevelZero = new HashSet<string>();
        private static int _stage;
        private static readonly Timer _unachableEviction = new Timer(_ =>
        {
            // Clear uncachable every 10 minutes
            if (_stage == 120)
            {
                _stage = 0;
                Logger.Write(LogLevel.Debug, () => $"Clearing {Uncachable.Count} uncachable stream names");
                Uncachable.Clear();
            }
            // Clear levelOne every minute
            if (_stage % 12 == 0)
                LevelOne.Clear();

            // Clear levelZero every 5 seconds
            LevelZero.Clear();

            _stage++;
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        private readonly bool _intelligent;
        private bool _disposed;

        public MemoryStreamCache(/*Boolean Intelligent = false*/)
        {
            _intelligent = false;
        }

        public void Cache(string stream, object cached)
        {
            if (_intelligent && (Uncachable.Contains(stream) || LevelOne.Contains(stream)))
                return;

            Logger.Write(LogLevel.Debug, () => $"Caching stream [{stream}]");
            var future = new DateTimeOffset().AddMinutes(1);
            MemCache.Set(stream, cached, new CacheItemPolicy { AbsoluteExpiration = future });
        }
        public void Evict(string stream)
        {
            Logger.Write(LogLevel.Debug, () => $"Evicting stream [{stream}] from cache");
            if (_intelligent)
            {
                if (Uncachable.Contains(stream)) return;

                if (LevelZero.Contains(stream))
                {
                    if (LevelOne.Contains(stream))
                    {
                        Logger.Write(LogLevel.Info, () => $"Stream [{stream}] has been evicted frequenty, marking uncachable for a few minutes");
                        Uncachable.Add(stream);
                    }
                    else
                        LevelOne.Add(stream);
                }
                else
                    LevelZero.Add(stream);

            }
            MemCache.Remove(stream);
        }
        public object Retreive(string stream)
        {
            var cached = MemCache.Get(stream);

            return cached;
        }

        public bool Update(string stream, object payload)
        {
            var cached = MemCache.Get(stream);

            if (!(cached is IEventStream) || !(payload is IWritableEvent)) return false;

            Logger.DebugFormat("Updating cached stream [{0}]", stream);
            var real = (cached as IEventStream);
            Cache(stream, real.Clone((IWritableEvent) payload));
            return true;
        }

        public void Dispose()
        {
            if(!_disposed)
                _unachableEviction.Dispose();
            _disposed = true;
        }
    }
}
