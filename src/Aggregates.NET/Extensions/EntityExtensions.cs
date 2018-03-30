using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Extensions
{
    static class EntityExtensions
    {
        public static string BuildParentsString(this IEntity entity)
        {
            return BuildParents(entity).Aggregate<Id, string>("", (cur, next) => $"{cur}:{next}");
        }
        public static Id[] BuildParents(this IEntity entity)
        {
            var list = entity.Parents?.ToList() ?? new List<Id>();
            list.Add(entity.Id);
            return list.ToArray();
        }
    }
}
