using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class DictionaryExtensions
    {
        public static void Merge<T, U>(this IDictionary<T, U> first, IDictionary<T, U> second)
        {
            foreach (var item in second)
                first[item.Key] = item.Value;
        }
    }
}