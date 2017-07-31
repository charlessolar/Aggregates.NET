using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Extensions
{
    static class EventSourceExtensions
    {
        public static string BuildParentsString(this IEventSource parent)
        {
            var parents = parent.Stream.Parents.ToList();
            parents.Add(parent.Id);
            return parents.BuildParentsString();
        }

        public static IEnumerable<Id> BuildParents(this IEventSource parent)
        {
            var parents = parent.Stream.Parents.ToList();
            parents.Add(parent.Id);
            return parents;
        }
    }
}
