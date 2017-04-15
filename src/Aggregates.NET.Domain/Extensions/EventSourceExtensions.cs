using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class EventSourceExtensions
    {
        public static string BuildParentsString(this IBase parent)
        {
            var parents = parent.Stream.Parents.ToList();
            parents.Add(parent.Id);
            return parents.Aggregate((cur, next) => $"{cur}/{next}");
        }

        public static IEnumerable<Id> BuildParents(this IBase parent)
        {
            var parents = parent.Stream.Parents.ToList();
            parents.Add(parent.Id);
            return parents;
        }
    }
}
