using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class DictionaryExtensions
    {
        public static IDictionary<T, U> Merge<T, U>(this IDictionary<T, U> first, IDictionary<T, U> second) where U : ICloneable
        {
            var result = new Dictionary<T, U>();

            foreach (var item in first)
                result[item.Key] = (U)item.Value.Clone();

            foreach (var item in second)
                result[item.Key] = (U)item.Value.Clone();
            return result;
        }
    }
}