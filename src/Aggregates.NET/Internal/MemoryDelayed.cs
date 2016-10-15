using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    internal class MemoryDelayed : IDelayedChannel
    {
        private static readonly ConcurrentDictionary<string, Tuple<DateTime, LinkedList<object>>> Store = new ConcurrentDictionary<string, Tuple<DateTime, LinkedList<object>>>();

        public Task<TimeSpan?> Age(string channel)
        {
            Tuple<DateTime, LinkedList<object>> existing;
            return Task.FromResult(!Store.TryGetValue(channel, out existing) ? (TimeSpan?)null : DateTime.UtcNow - existing.Item1);
        }

        public Task<int> Size(string channel)
        {
            Tuple<DateTime, LinkedList<object>> existing;
            return Task.FromResult(!Store.TryGetValue(channel, out existing) ? 0 : existing.Item2.Count);
        }

        public Task<int> AddToQueue(string channel, object queued)
        {
            var count = 1;
            Store.AddOrUpdate(channel, (_) =>
            {
                var existing = new LinkedList<object>();
                existing.AddLast(queued);
                return new Tuple<DateTime, LinkedList<object>>(DateTime.UtcNow, existing);
            }, (_, existing) =>
            {
                existing.Item2.AddLast(queued);
                count = existing.Item2.Count;
                return existing;
            });
            return Task.FromResult(count);
        }

        public Task<IEnumerable<object>> Pull(string channel)
        {
            Tuple<DateTime, LinkedList<object>> existing;
            return Task.FromResult(!Store.TryRemove(channel, out existing) ? new object[] {}.AsEnumerable() : existing.Item2.AsEnumerable());
        }
    }
}
