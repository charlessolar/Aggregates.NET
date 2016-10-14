using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    internal class MemoryDelayed : IDelayedChannel
    {
        private static readonly ConcurrentDictionary<string, LinkedList<object>> Store = new ConcurrentDictionary<string, LinkedList<object>>();

        public Task<int> Size(string channel)
        {
            LinkedList<object> existing;
            return Task.FromResult(!Store.TryGetValue(channel, out existing) ? 0 : existing.Count);
        }

        public Task<int> AddToQueue(string channel, object queued)
        {
            var count = 1;
            Store.AddOrUpdate(channel, (_) =>
            {
                var existing = new LinkedList<object>();
                existing.AddLast(queued);
                return existing;
            }, (_, existing) =>
            {
                existing.AddLast(queued);
                count = existing.Count;
                return existing;
            });
            return Task.FromResult(count);
        }

        public Task<IEnumerable<object>> Pull(string channel)
        {
            LinkedList<object> existing;
            return Task.FromResult(!Store.TryRemove(channel, out existing) ? new object[] {}.AsEnumerable() : existing.AsEnumerable());
        }
    }
}
