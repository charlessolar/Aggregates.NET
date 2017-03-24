using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using NServiceBus.Extensibility;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    internal class MemoryPersistence : IPersistence
    {
        private static readonly ILog Logger = LogManager.GetLogger("MemoryPersistence");
        private static readonly Counter Size = Metric.Counter("UOW Bags Persisted", Unit.Items, tags: "debug");
        private static readonly ConcurrentDictionary<string, List<Tuple<Type, ContextBag>>> Storage = new ConcurrentDictionary<string, List<Tuple<Type, ContextBag>>>();

        public Task Save(string messageId, Type uowType, ContextBag bag)
        {
            Logger.Write(LogLevel.Debug, () => $"Persisting context bag for message [{messageId}] uow [{uowType.FullName}]");
            Storage.AddOrUpdate(messageId,
                x =>
                {
                    Size.Increment();
                    return new List<Tuple<Type, ContextBag>> {new Tuple<Type, ContextBag>(uowType, bag)};
                },
                (key, existing) =>
                {
                    var existingBag = existing.SingleOrDefault(x => x.Item1 == uowType);
                    if(existingBag != null)
                        existing.Remove(existingBag);
                    else
                        Size.Increment();

                    existing.Add(new Tuple<Type, ContextBag>(uowType, bag));
                    return existing;
                });
            return Task.CompletedTask;
        }

        public Task<List<Tuple<Type, ContextBag>>> Remove(string messageId)
        {
            Logger.Write(LogLevel.Debug, () => $"Removing persisted context bags for message [{messageId}]");
            List<Tuple<Type, ContextBag>> existing;
            if (!Storage.TryRemove(messageId, out existing))
                return Task.FromResult<List<Tuple<Type, ContextBag>>>(null);


            Size.Decrement(existing.Count);
            return Task.FromResult(existing);

            //var bag = existing.SingleOrDefault(x => x.Item1 == uowType);

            
            //foreach (var other in existing)
            //{
            //    if (bag != null && other.Item1 == bag.Item1)
            //        continue;
            //    Save(messageId, other.Item1, other.Item2);
            //}

            //return Task.FromResult(bag?.Item2);
        }
        
    }
}
