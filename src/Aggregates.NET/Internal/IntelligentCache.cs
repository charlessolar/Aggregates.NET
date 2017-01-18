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

        private static readonly Dictionary<string, object> MemCache = new Dictionary<string, object>();

        private static readonly HashSet<string> Expires0 = new HashSet<string>();
        private static readonly HashSet<string> Expires1 = new HashSet<string>();
        private static readonly HashSet<string> Expires2 = new HashSet<string>();
        // Only cache items that don't change very often
        private static readonly HashSet<string> Cachable = new HashSet<string>();
        private static readonly HashSet<string> RecentEvictions = new HashSet<string>();
        private static readonly object Lock = new object();
        private static int _stage;
        private static readonly Timer CachableEviction = new Timer(_ =>
        {
            // Clear cachable every 10 minutes
            if (_stage % 120 == 0)
            {
                _stage = 0;
                Logger.Write(LogLevel.Debug, () => $"Clearing {Cachable.Count} cachable keys");

                lock (Lock)
                {
                    foreach (var stream in Cachable)
                        MemCache.Remove(stream);
                    Cachable.Clear();
                }
            }

            if (_stage % 60 == 0)
            {
                Logger.Write(LogLevel.Debug, () => $"Clearing {Expires2.Count} 5m cached keys");
                lock (Lock)
                {
                    foreach (var stream in Expires2)
                        MemCache.Remove(stream);
                    Expires2.Clear();
                }
            }
            if (_stage % 12 == 0)
            {
                Logger.Write(LogLevel.Debug, () => $"Clearing {Expires1.Count} 1m cached keys");
                lock (Lock)
                {
                    foreach (var stream in Expires1)
                        MemCache.Remove(stream);
                    Expires1.Clear();
                    RecentEvictions.Clear();
                }
            }

            if (_stage % 2 == 0)
            {
                Logger.Write(LogLevel.Debug, () => $"Clearing {Expires0.Count} 10s cached keys");
                lock (Lock)
                {
                    foreach (var stream in Expires0)
                        MemCache.Remove(stream);
                    Expires0.Clear();
                }
            }
            
            _stage++;
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        private bool _disposed;
        

        public void Cache(string key, object cached, bool expires10S = false, bool expires1M = false, bool expires5M = false)
        {
            lock (Lock)
            {

                if (Cachable.Contains(key) || expires10S || expires1M || expires5M)
                {
                    if (!expires10S && !expires1M && !expires5M)
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}]");
                    else if (expires10S)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}] expires in 10s");
                        Expires0.Add(key);
                    }
                    else if (expires1M)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}] expires in 1m");
                        Expires1.Add(key);
                    }else if (expires5M)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Caching item [{key}] expires in 5m");
                        Expires2.Add(key);
                    }
                    MemCache[key] = cached;

                    return;
                }

                if (!RecentEvictions.Contains(key))
                {
                    Logger.Write(LogLevel.Info,
                        () =>
                                $"Stream [{key}] has not been evicted recently, marking cachable for a few minutes");
                    Cachable.Add(key);
                }

            }

        }
        public void Evict(string key)
        {
            Logger.Write(LogLevel.Debug, () => $"Evicting item [{key}] from cache");

            lock (Lock)
            {
                RecentEvictions.Add(key);
                Cachable.Remove(key);

                MemCache.Remove(key);
            }
            

        }
        public object Retreive(string key)
        {
            object cached;
            lock (Lock)
            {
                // Cachable check is O(1) whereas a Dict search is not
                if (!Cachable.Contains(key) || !MemCache.TryGetValue(key, out cached))
                    cached = null;
            }
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
