using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IDelayedChannel 
    {
        /// <summary>
        /// Gets the age of a specific delayed channel, checks the age of channel + key and channel - returns oldest
        /// </summary>
        Task<TimeSpan?> Age(string channel, string key = null);

        /// <summary>
        /// Gets the size of a specific delayed channel, checks the size of channel + key and channel - returns sum
        /// </summary>
        Task<int> Size(string channel, string key = null);

        /// <summary>
        /// Adds an object to be delayed, channel is the durable storage id, key is memory cache id
        /// Objects will be optionally cached in memory by channel + key until a flush which commits to channel (loses the key)
        /// </summary>
        Task AddToQueue(string channel, object queued, string key = null);

        /// <summary>
        /// Pulls all delayed objects at channel + key and all objects in durable storage at channel
        /// </summary>
        Task<IEnumerable<object>> Pull(string channel, string key = null, int? max=null);
        
    }
}
