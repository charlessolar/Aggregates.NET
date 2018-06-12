using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    static class TaskExtensions
    {
        // https://blogs.msdn.microsoft.com/pfxteam/2012/03/05/implementing-a-simple-foreachasync-part-2/
        public static Task StartEachAsync<T>(
         this T[] source, int dop, Func<T, Task> body)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source, loadBalance: true).GetPartitions(dop)
                select Task.Run(async () =>
                {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current).ConfigureAwait(false);

                }));
        }

        // http://compiledexperience.com/blog/posts/async-extensions/
        public static Task WhenAllAsync<T>(this IEnumerable<T> values, Func<T, Task> asyncAction)
        {
            return Task.WhenAll(values.Select(asyncAction));
        }

    }
}
