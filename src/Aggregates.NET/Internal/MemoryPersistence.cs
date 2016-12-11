using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Extensibility;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    internal class MemoryPersistence : IPersistence
    {
        private static readonly ILog Logger = LogManager.GetLogger("MemoryPersistence");
        private static readonly ConcurrentDictionary<string, ContextBag> Storage = new ConcurrentDictionary<string, ContextBag>();

        public Task Save(string id, ContextBag bag)
        {
            Logger.Write(LogLevel.Debug, () => $"Persisting context bag [{id}]");
            Storage.TryAdd(id, bag);
            return Task.CompletedTask;
        }

        public Task<ContextBag> Remove(string id)
        {
            Logger.Write(LogLevel.Debug, () => $"Removing context bag [{id}]");
            ContextBag existing;
            if (!Storage.TryRemove(id, out existing))
                return Task.FromResult<ContextBag>(null);
            return Task.FromResult(existing);
        }
    }
}
