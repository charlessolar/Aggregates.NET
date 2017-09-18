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
            if (entity.Parents == null || !entity.Parents.Any())
                return "";
            return entity.Parents.Aggregate<Id, string>("", (cur, next) => $"{cur}:{next}");
        }
        public static Id[] BuildParents(this IEntity entity)
        {
            var list = entity.Parents?.ToList() ?? new List<Id>();
            list.Add(entity.Id);
            return list.ToArray();
        }
    }
}
