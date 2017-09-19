using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;

namespace Aggregates
{
    public class MutationManager
    {
        private static readonly ILog Logger = LogProvider.GetLogger("MutationManager");

        private static readonly ConcurrentDictionary<string, Type> Mutators = new ConcurrentDictionary<string, Type>();

        public static void RegisterMutator(string id, Type mutator)
        {
            //if (!typeof(IMutate).IsAssignableFrom(mutator))
            //    throw new ArgumentException($"Mutator {id} type {mutator.FullName} does not implement IMutate");

            Mutators.TryAdd(id, mutator);
        }

        public static void DeregisterMutator(string id)
        {
            Type temp;
            Mutators.TryRemove(id, out temp);
        }

        public static IEnumerable<Type> Registered => Mutators.Values;
    }
}
