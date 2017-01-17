using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Aggregates.Extensions
{
    static class StackExtensions
    {

        public static IEnumerable<T> PopAll<T>( this ConcurrentStack<T> source)
        {
            while (source.Count > 0)
            {
                T popped;
                if (!source.TryPop(out popped))
                    break;

                yield return popped;
            }
        }
        public static IEnumerable<T> PopAll<T>(this Stack<T> source)
        {
            while (source.Count > 0)
                yield return source.Pop();
            
        }

    }
}
