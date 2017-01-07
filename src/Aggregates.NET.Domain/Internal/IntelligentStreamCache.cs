using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    // Only actually caches a stream if the stream is read several times without writing to it
    class IntelligentStreamCache : IStreamCache, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("MemoryStreamCache");

        private static readonly ConcurrentDictionary<string, object> MemCache =
            new ConcurrentDictionary<string, object>();

        private static readonly HashSet<string> Expires0 = new HashSet<string>();
        private static readonly HashSet<string> Expires1 = new HashSet<string>();
        // Only cache streams that don't change very often
        private static readonly HashSet<string> Cachable = new HashSet<string>();
        private static readonly HashSet<string> LevelOne = new HashSet<string>();
        private static readonly HashSet<string> LevelZero = new HashSet<string>();
        private static readonly object Lock = new object();
        private static int _stage;
        private static readonly Timer CachableEviction = new Timer(_ =>
        {
            // Clear cachable every 10 minutes
            if (_stage == 120)
            {
                _stage = 0;
                Logger.Write(LogLevel.Debug, () => $"Clearing {Cachable.Count} uncachable stream names");

                lock (Lock)
                {
                    Cachable.Clear();
                    object e;
                    foreach (var stream in Expires1)
                        MemCache.TryRemove(stream, out e);
                    Expires1.Clear();
                }
            }
            // Clear levelOne every 10 seconds
            if (_stage % 2 == 0)
            {
                lock (Lock)
                {
                    LevelOne.Clear();
                    object e;
                    foreach (var stream in Expires0)
                        MemCache.TryRemove(stream, out e);
                    Expires0.Clear();
                }
            }

            // Clear levelZero every 5 seconds
            LevelZero.Clear();

            _stage++;
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        private bool _disposed;

        public IntelligentStreamCache()
        {
        }

        public void Cache(string stream, object cached, bool expires10S = false, bool expires1M = false)
        {
            lock (Lock)
            {
                if (Cachable.Contains(stream) || expires10S || expires1M)
                {
                    if (!expires10S && !expires1M)
                        Logger.Write(LogLevel.Debug, () => $"Caching stream [{stream}]");
                    else if (expires10S)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching stream [{stream}] expires in 10s");
                        lock (Lock) Expires0.Add(stream);
                    }
                    else if (expires1M)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching stream [{stream}] expires in 1m");
                        lock (Lock) Expires1.Add(stream);
                    }
                    MemCache.AddOrUpdate(stream, (_) => cached, (_, e) => cached);

                    return;
                }

                if (LevelZero.Contains(stream))
                {
                    if (LevelOne.Contains(stream))
                    {
                        Logger.Write(LogLevel.Info,
                            () =>
                                    $"Stream [{stream}] has been read frequenty, marking cachable for a few minutes");
                        Cachable.Add(stream);
                    }
                    else
                        LevelOne.Add(stream);
                }
                else
                    LevelZero.Add(stream);

            }




        }
        public void Evict(string stream)
        {
            Logger.Write(LogLevel.Debug, () => $"Evicting stream [{stream}] from cache");

            lock (Lock)
            {
                LevelOne.Remove(stream);
                LevelZero.Remove(stream);
                Cachable.Remove(stream);
            }

            object e;
            MemCache.TryRemove(stream, out e);

        }
        public object Retreive(string stream)
        {
            object cached;
            if (!MemCache.TryGetValue(stream, out cached))
                cached = null;
            if (cached == null)
                Logger.Write(LogLevel.Debug, () => $"Stream [{stream}] is not in cache");

            return cached;
        }

        public void Dispose()
        {
            if (!_disposed)
                CachableEviction.Dispose();
            _disposed = true;
        }
    }
}
