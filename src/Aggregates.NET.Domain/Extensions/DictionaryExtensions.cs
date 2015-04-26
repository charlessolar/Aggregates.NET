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
            return first.Concat(second).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}