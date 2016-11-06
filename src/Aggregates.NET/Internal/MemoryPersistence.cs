using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus.Extensibility;

namespace Aggregates.Internal
{
    internal class MemoryPersistence : IPersistence
    {
        private static readonly Dictionary<string, ContextBag> Storage = new Dictionary<string, ContextBag>();

        public Task Save(string id, ContextBag bag)
        {
            Storage[id] = bag;
            return Task.CompletedTask;
        }

        public Task<ContextBag> Remove(string id)
        {
            ContextBag existing;
            if (!Storage.TryGetValue(id, out existing))
                return Task.FromResult<ContextBag>(null);

            Storage.Remove(id);
            return Task.FromResult(existing);
        }
    }
}
