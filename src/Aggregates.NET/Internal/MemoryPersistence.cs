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
        private static readonly ConcurrentDictionary<string, List<Tuple<Type, ContextBag>>> Storage = new ConcurrentDictionary<string, List<Tuple<Type, ContextBag>>>();

        public Task Save(string messageId, Type uowType, ContextBag bag)
        {
            Logger.Write(LogLevel.Debug, () => $"Persisting context bag for message [{messageId}] uow [{uowType.FullName}]");
            Storage.AddOrUpdate(messageId,
                x => new List<Tuple<Type, ContextBag>> {new Tuple<Type, ContextBag>(uowType, bag)},
                (key, existing) =>
                {
                    existing.Add(new Tuple<Type, ContextBag>(uowType, bag));
                    return existing;
                });
            return Task.CompletedTask;
        }

        public Task<ContextBag> Remove(string messageId, Type uowType)
        {
            Logger.Write(LogLevel.Debug, () => $"Persisting context bag for message [{messageId}] uow [{uowType.FullName}]");
            List<Tuple<Type, ContextBag>> existing;
            if (!Storage.TryRemove(messageId, out existing))
                return Task.FromResult<ContextBag>(null);
            
            return Task.FromResult(existing.SingleOrDefault(x => x.Item1 == uowType)?.Item2);
        }

        public Task Clear(string messageId)
        {
            List<Tuple<Type, ContextBag>> existing;
            Storage.TryRemove(messageId, out existing);

            return Task.CompletedTask;
        }
    }
}
