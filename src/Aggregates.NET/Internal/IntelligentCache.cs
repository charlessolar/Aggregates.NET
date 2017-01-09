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
    // Only actually caches an item if the item is cached several times without evicting it
    class IntelligentCache : ICache, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("IntelligentCache");

        private static readonly ConcurrentDictionary<string, object> MemCache =
            new ConcurrentDictionary<string, object>();

        private static readonly HashSet<string> Expires0 = new HashSet<string>();
        private static readonly HashSet<string> Expires1 = new HashSet<string>();
        // Only cache items that don't change very often
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
                Logger.Write(LogLevel.Debug, () => $"Clearing {Cachable.Count} cachable keys");

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

        public IntelligentCache()
        {
        }

        public void Cache(string key, object cached, bool expires10S = false, bool expires1M = false)
        {
            lock (Lock)
            {
                if (Cachable.Contains(key) || expires10S || expires1M)
                {
                    if (!expires10S && !expires1M)
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}]");
                    else if (expires10S)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}] expires in 10s");
                        lock (Lock) Expires0.Add(key);
                    }
                    else if (expires1M)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}] expires in 1m");
                        lock (Lock) Expires1.Add(key);
                    }
                    MemCache.AddOrUpdate(key, (_) => cached, (_, e) => cached);

                    return;
                }

                if (LevelZero.Contains(key))
                {
                    if (LevelOne.Contains(key))
                    {
                        Logger.Write(LogLevel.Info,
                            () =>
                                    $"Stream [{key}] has been cahed frequenty, marking cachable for a few minutes");
                        Cachable.Add(key);
                    }
                    else
                        LevelOne.Add(key);
                }
                else
                    LevelZero.Add(key);

            }




        }
        public void Evict(string key)
        {
            Logger.Write(LogLevel.Debug, () => $"Evicting item [{key}] from cache");

            lock (Lock)
            {
                LevelOne.Remove(key);
                LevelZero.Remove(key);
                Cachable.Remove(key);
            }

            object e;
            MemCache.TryRemove(key, out e);

        }
        public object Retreive(string key)
        {
            object cached;
            if (!MemCache.TryGetValue(key, out cached))
                cached = null;
            if (cached == null)
                Logger.Write(LogLevel.Debug, () => $"Item [{key}] is not in cache");

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
