using System.Collections.Generic;

namespace Aggregates.Extensions
{
    public static class DictionaryExtensions
    {
        public static IDictionary<T, TU> Merge<T, TU>(this IDictionary<T, TU> first, IDictionary<T, TU> second)
        {
            var result = new Dictionary<T, TU>(first);
            
            foreach (var item in second)
                result[item.Key] = item.Value;
            return result;
        }
    }
}