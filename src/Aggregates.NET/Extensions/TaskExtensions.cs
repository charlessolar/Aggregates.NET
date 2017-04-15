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

        // http://compiledexperience.com/blog/posts/async-extensions/
        public static Task<TResult[]> SelectAsync<TSource, TResult>(this IEnumerable<TSource> values, Func<TSource, Task<TResult>> asyncSelector)
        {
            return Task.WhenAll(values.Select(asyncSelector));
        }
        public static async Task WhenAllSync<T>(this IEnumerable<T> values, Func<T, Task> asyncAction)
        {
            foreach (var val in values)
                await asyncAction.Invoke(val).ConfigureAwait(false);
        }

        public static async Task WhileAsync<TSource>(this ICollection<TSource> values, Func<TSource, Task> asyncSelector)
        {
            while (values.Any())
            {
                var value = values.First();
                values.Remove(value);
                await asyncSelector.Invoke(value).ConfigureAwait(false);
            }
        }
        public static async Task WhileAsync<TKey, TValue>(this ICollection<KeyValuePair<TKey, TValue>> values, Func<KeyValuePair<TKey, TValue>, Task> asyncSelector)
        {
            while (values.Any())
            {
                var value = values.First();
                values.Remove(value);
                await asyncSelector.Invoke(value).ConfigureAwait(false);
            }
        }
        public static async Task WhileAsync<TKey, TValue>(this ICollection<KeyValuePair<TKey, TValue>> values, Func<KeyValuePair<TKey,TValue>, bool> whereSelector, Func<KeyValuePair<TKey, TValue>, Task> asyncSelector)
        {
            while (values.Any(whereSelector))
            {
                var value = values.First(whereSelector);
                values.Remove(value);
                await asyncSelector.Invoke(value).ConfigureAwait(false);
            }
        }

    }
}
