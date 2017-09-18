using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface ICache
    {
        void Cache(string key, object cached, bool expires10S = false, bool expires1M = false, bool expires5M = false);
        object Retreive(string key);

        void Evict(string key);
    }
}
