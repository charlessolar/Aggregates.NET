using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IDelayedChannel
    {
        Task<TimeSpan?> Age(String channel);

        Task<int> Size(string channel);

        Task<int> AddToQueue(string channel, object queued);

        Task<IEnumerable<object>> Pull(string channel);
    }
}
