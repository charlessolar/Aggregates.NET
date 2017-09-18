using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;

namespace Aggregates.Internal
{
    class DelayedChannel : IDelayedChannel
    {
        private class InFlightInfo
        {
            public DateTime At { get; set; }
            public int Position { get; set; }
        }


        private static readonly ILog Logger = LogProvider.GetLogger("EventStoreDelayed");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");
        
        private readonly IDelayedCache _cache;
        
        private readonly object _lock = new object();
        private Dictionary<Tuple<string, string>, List<IDelayedMessage>> _inFlightMemCache;
        private Dictionary<Tuple<string, string>, List<IDelayedMessage>> _uncommitted;


        public DelayedChannel(IDelayedCache cache)
        {
            _cache = cache;
        }
        

        public Task Begin()
        {
            _uncommitted = new Dictionary<Tuple<string, string>, List<IDelayedMessage>>();
            _inFlightMemCache = new Dictionary<Tuple<string, string>, List<IDelayedMessage>>();
            return Task.CompletedTask;
        }

        public async Task End(Exception ex = null)
        {

            if (ex != null)
            {
                Logger.Write(LogLevel.Debug, () => $"UOW exception - putting {_inFlightMemCache.Count()} in flight channels back into memcache");
                foreach (var inflight in _inFlightMemCache)
                {
                    await _cache.Add(inflight.Key.Item1, inflight.Key.Item2, inflight.Value.ToArray()).ConfigureAwait(false);                    
                }
            }

            if (ex == null)
            {
                Logger.Write(LogLevel.Debug, () => $"Putting {_uncommitted.Count()} delayed streams into mem cache");

                _inFlightMemCache.Clear();

                foreach( var kv in _uncommitted)
                {
                    if (!kv.Value.Any())
                        return;

                    await _cache.Add(kv.Key.Item1, kv.Key.Item2, kv.Value.ToArray());
                }
            }
        }

        public async Task<TimeSpan?> Age(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}] key [{key}]");

            var specificAge = await _cache.Age(channel, key).ConfigureAwait(false);
            
            if (specificAge > TimeSpan.FromMinutes(5))
                SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] specific [{key}] is {specificAge?.TotalMinutes} minutes old!");

            return specificAge;
        }

        public async Task<int> Size(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}] key [{key}]");

            var specificSize = await _cache.Size(channel, key).ConfigureAwait(false);

            var specificKey = new Tuple<string, string>(channel, key);
            if (_uncommitted.ContainsKey(specificKey))
                specificSize += _uncommitted[specificKey].Count;
            
            if (specificSize > 5000)
                SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] specific [{key}] size is {specificSize}!");

            return specificSize;
        }

        public Task AddToQueue(string channel, IDelayedMessage queued, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Appending delayed object to channel [{channel}] key [{key}]");

            if (queued == null)
            {
                Logger.Write(LogLevel.Warn, () => $"Adding NULL queued object! Stack: {Environment.StackTrace}");
                return Task.CompletedTask;
            }

            var specificKey = new Tuple<string, string>(channel, key);
            lock (_lock)
            {
                if (!_uncommitted.ContainsKey(specificKey))
                    _uncommitted[specificKey] = new List<IDelayedMessage>();
                _uncommitted[specificKey].Add(queued);
            }


            return Task.CompletedTask;
        }

        public async Task<IEnumerable<IDelayedMessage>> Pull(string channel, string key = null, int? max = null)
        {
            var specificKey = new Tuple<string, string>(channel, key);

            Logger.Write(LogLevel.Debug, () => $"Pulling delayed channel [{channel}] key [{key}] max [{max}]");

            var fromCache = await _cache.Pull(channel, key, max).ConfigureAwait(false);

            List<IDelayedMessage> discovered = new List<IDelayedMessage>(fromCache);

            List<IDelayedMessage> fromUncommitted;
            if (_uncommitted.TryGetValue(specificKey, out fromUncommitted))
                discovered.AddRange(fromUncommitted);
            
            lock (_lock) _inFlightMemCache.Add(specificKey, discovered);
            
            Logger.Write(LogLevel.Info, () => $"Pulled {discovered.Count} from delayed channel [{channel}] key [{key}]");
            return discovered;
        }


    }
}
