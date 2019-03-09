using Aggregates.Contracts;
using Aggregates.Internal;
using System;
using System.Linq;

namespace Aggregates.Extensions
{
    static class StringExtensions
    {
        public static string MaxLength(this string source, int max)
        {
            if (source.Length < max)
                return source;
            return source.Substring(0, max);
        }
        public static string MaxLines(this string source, int max)
        {
            var ret = "";
            var lines = 0;
            while(lines < max)
            {
                var newline = source.IndexOf("\n", StringComparison.Ordinal);
                if (newline == -1)
                {
                    ret += source;
                    break;
                }
                ret += source.Substring(0, newline + 1);
                source = source.Substring(newline + 1);
                lines++;
            }

            return ret.Trim();
        }
        // dotnet core GetHashCode returns different values (not deterministic)
        // we just need a simple deterministic hash
        public static int GetHash(this string stream)
        {
            // Unchecked, allow overflow
            unchecked
            {
                int hash = 23;
                foreach (char c in stream)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
        public static string Flatten(this Id[] ids)
        {
            if (ids == null || !ids.Any())
                return "";
            return ids.Aggregate((cur, next) => $"{cur}:{next}");
        }
        public static Id[] GetParentIds(this IEntity entity)
        {
            if (!(entity is IChildEntity))
                return new Id[] { };
            var parents = (entity as IChildEntity).Parent.GetParentIds().ToList();
            parents.Add((entity as IChildEntity).Parent.Id);
            return parents.ToArray();
        }
    }
}
