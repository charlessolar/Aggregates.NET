using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class DictionaryExtensions
    {
        public static IDictionary<T, U> Merge<T, U>(this IDictionary<T, U> first, IDictionary<T, U> second)
        {
            var result = new Dictionary<T, U>(first);
            
            foreach (var item in second)
                result[item.Key] = item.Value;
            return result;
        }
    }
}