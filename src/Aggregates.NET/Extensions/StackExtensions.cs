using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Aggregates.Extensions
{
    public static class StackExtensions
    {

        public static IEnumerable<T> Generate<T>( this ConcurrentStack<T> source)
        {
            while (source.Count > 0)
            {
                T popped;
                if (!source.TryPop(out popped))
                    break;

                yield return popped;
            }
        }
        public static IEnumerable<T> Generate<T>(this Stack<T> source)
        {
            while (source.Count > 0)
                yield return source.Pop();
            
        }

    }
}
