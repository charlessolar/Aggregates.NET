using System.Collections.Generic;
using System.Linq;

namespace Aggregates.Extensions
{
    static class DictionaryExtensions
    {
        public static Dictionary<T, TU> Merge<T, TU>(this IDictionary<T, TU> first, IDictionary<T, TU> second)
        {
            var result = new Dictionary<T, TU>(first);
            
            foreach (var item in second)
                result[item.Key] = item.Value;
            return result;
        }
        public static string AsString<T, TU>(this IDictionary<T, TU> dict)
        {
            return "{ " + dict.Select(kv => $"({kv.Key}): [{kv.Value}]").Aggregate((cur, next) => $"{cur}, {next}") + " }";
        }
    }
}