using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IDelayedCache
    {
        Task Add(string channel, string key, IDelayedMessage[] messages);
        Task<IDelayedMessage[]> Pull(string channel, string key = null, int? max = null);

        Task<TimeSpan?> Age(string channel, string key);
        Task<int> Size(string channel, string key);
    }
}
