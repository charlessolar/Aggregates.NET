using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    internal class MemoryDelayed : IDelayedChannel
    {
        private static readonly ConcurrentDictionary<string, List<object>> Store = new ConcurrentDictionary<string, List<object>>();

        public Task<int> Size(string channel)
        {
            List<object> existing;
            return Task.FromResult(!Store.TryGetValue(channel, out existing) ? 0 : existing.Count);
        }

        public Task AddToQueue(string channel, object queued)
        {
            Store.AddOrUpdate(channel, (_) => new List<object> { queued }, (_, existing) =>
            {
                existing.Add(queued);
                return existing;
            });
            return Task.CompletedTask;
        }

        public Task<IEnumerable<object>> Pull(string channel)
        {
            List<object> existing;
            return Task.FromResult(!Store.TryRemove(channel, out existing) ? new object[] {}.AsEnumerable() : existing.AsEnumerable());
        }
    }
}
