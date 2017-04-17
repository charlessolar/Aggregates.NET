using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    static class EnumerableExtensions
    {
        public static int FirstIndex<T>(this IEnumerable<T> source, Func<T, bool> selector)
        {
            for (var i = 0; i < source.Count(); i++)
                if (selector(source.ElementAt(i)))
                    return i;
            return -1;
        }
    }
}
