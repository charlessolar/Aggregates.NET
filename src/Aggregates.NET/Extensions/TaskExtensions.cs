using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class TaskExtensions
    {
        public static Task ForEachAsync<T>(
         this IEnumerable<T> source, int dop, Func<T, Task> body)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(dop)
                select Task.Run(async delegate
                {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current);

                }));
        }

        // http://compiledexperience.com/blog/posts/async-extensions/
        public static Task WhenAllAsync<T>(this IEnumerable<T> values, Func<T, Task> asyncAction)
        {
            return Task.WhenAll(values.Select(asyncAction));
        }

        // http://compiledexperience.com/blog/posts/async-extensions/
        public static Task<TResult[]> SelectAsync<TSource, TResult>(this IEnumerable<TSource> values, Func<TSource, Task<TResult>> asyncSelector)
        {
            return Task.WhenAll(values.Select(asyncSelector));
        }
        public static async Task WhenAllSync<T>(this IEnumerable<T> values, Func<T, Task> asyncAction)
        {
            foreach (var val in values)
                await asyncAction.Invoke(val);
        }
    }
}
