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
    public class MemoryStreamCache : IStreamCache, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MemoryStreamCache));

        private static readonly ConcurrentDictionary<string, object> MemCache =
            new ConcurrentDictionary<string, object>();


        // For streams that are changing multiple times a second, no sense to cache them if they get immediately evicted
        private static readonly HashSet<string> Uncachable = new HashSet<string>();
        private static readonly HashSet<string> LevelOne = new HashSet<string>();
        private static readonly HashSet<string> LevelZero = new HashSet<string>();
        private static int _stage;
        private static readonly Timer _unachableEviction = new Timer(_ =>
        {
            // Clear uncachable every 1 minute
            if (_stage == 12)
            {
                _stage = 0;
                Logger.Write(LogLevel.Debug, () => $"Clearing {Uncachable.Count} uncachable stream names");
                Uncachable.Clear();
            }
            // Clear levelOne every 10 seconds
            if (_stage % 2 == 0)
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
            MemCache.AddOrUpdate(stream, (_) => cached, (_, e) => cached);
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
            object e;
            MemCache.TryRemove(stream, out e);
        }
        public object Retreive(string stream)
        {
            object cached;
            if (!MemCache.TryGetValue(stream, out cached))
                cached = null;
            if(cached == null)
                Logger.Write(LogLevel.Debug, () => $"Stream [{stream}] is not in cache");

            return cached;
        }

        public bool Update(string stream, object payload)
        {
            var cached = Retreive(stream);

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
