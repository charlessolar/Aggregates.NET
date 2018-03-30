using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;

namespace Aggregates.Internal
{
    // Only actually caches an item if the item is cached several times without evicting it
    class IntelligentCache : ICache
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Cache");

        private static readonly Dictionary<string, object> MemCache = new Dictionary<string, object>();

        private static readonly HashSet<string> Expires0 = new HashSet<string>();
        private static readonly HashSet<string> Expires1 = new HashSet<string>();
        private static readonly HashSet<string> Expires2 = new HashSet<string>();
        // Only cache items that don't change very often
        private static readonly HashSet<string> Cachable = new HashSet<string>();
        //                                          Last attempt  count  
        private static readonly Dictionary<string, Tuple<DateTime, int>> CacheAttempts = new Dictionary<string, Tuple<DateTime, int>>();
        private static readonly object Lock = new object();
        private static int _stage;

        private static int _evicting;
        private static Task _eviction;

        public IntelligentCache(IMetrics metrics)
        {
            if (Interlocked.CompareExchange(ref _evicting, 1, 0) == 1) return;

            _eviction = Timer.Repeat((state) =>
            {
                var m = state as IMetrics;

                // Clear cachable every 10 minutes
                if (_stage % 120 == 0)
                {
                    _stage = 0;

                    lock (Lock)
                    {
                        foreach (var expired in CacheAttempts.Where(x => (DateTime.UtcNow - x.Value.Item1).TotalMinutes > 10).Select(x => x.Key).ToList())
                        {
                            CacheAttempts.Remove(expired);
                            MemCache.Remove(expired);
                        }
                    }
                }

                if (_stage % 60 == 0)
                {
                    lock (Lock)
                    {
                        foreach (var stream in Expires2)
                            MemCache.Remove(stream);
                        Expires2.Clear();
                    }
                }
                if (_stage % 12 == 0)
                {
                    lock (Lock)
                    {
                        foreach (var stream in Expires1)
                            MemCache.Remove(stream);
                        Expires1.Clear();
                    }
                }

                if (_stage % 2 == 0)
                {
                    lock (Lock)
                    {
                        foreach (var stream in Expires0)
                            MemCache.Remove(stream);
                        Expires0.Clear();
                    }
                }

                lock (Lock) m.Update("Aggregates Cache Size", Unit.Items, MemCache.Count);


                _stage++;
                return Task.CompletedTask;
            }, metrics, TimeSpan.FromSeconds(5), "intelligent cache eviction");
        }

        public void Cache(string key, object cached, bool expires10S = false, bool expires1M = false, bool expires5M = false)
        {
            lock (Lock)
            {
                if (Cachable.Contains(key) || expires10S || expires1M || expires5M)
                {
                    if (!expires10S && !expires1M && !expires5M)
                        Logger.DebugEvent("Cache", "{Key}", key);
                    else if (expires10S)
                    {
                        Logger.DebugEvent("Cache", "{Key} for {Seconds}s", key, 10);
                        Expires0.Add(key);
                    }
                    else if (expires1M)
                    {
                        Logger.DebugEvent("Cache", "{Key} for {Seconds}s", key, 60);
                        Expires1.Add(key);
                    }
                    else if (expires5M)
                    {
                        Logger.DebugEvent("Cache", "{Key} for {Seconds}s", key, 300);
                        Expires2.Add(key);
                    }

                    MemCache[key] = cached;

                    return;
                }

                if (!CacheAttempts.ContainsKey(key))
                    CacheAttempts[key] = new Tuple<DateTime, int>(DateTime.UtcNow, 1);
                else
                {
                    CacheAttempts[key] =
                        new Tuple<DateTime, int>(DateTime.UtcNow, Math.Min(20, CacheAttempts[key].Item2 + 1));

                    if (CacheAttempts[key].Item2 >= 20)
                    {
                        Logger.DebugEvent("Cachable", "{Key} is cachable now", key);
                        Cachable.Add(key);
                    }
                }
            }




        }
        public void Evict(string key)
        {
            lock (Lock)
            {
                // Decrease by 5 - evicting is a terrible thing, usually means there was a version conflict
                if (CacheAttempts.ContainsKey(key))
                    CacheAttempts[key] = new Tuple<DateTime, int>(DateTime.UtcNow, Math.Max(0, CacheAttempts[key].Item2 - 5));

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
                    return null;
            }

            Logger.DebugEvent("Retrieved", "{Key}", key);
            return cached;
        }

    }
}
