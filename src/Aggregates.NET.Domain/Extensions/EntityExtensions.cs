using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates.Extensions
{
    public static class EntityExtensions
    {
        /// <summary>
        /// Cast all events to T - will fail if any are not type T
        /// </summary>
        public static IEnumerable<T> Cast<T>(this IEnumerable<IFullEvent> events) where T : IEvent
        {
            return events.Select(x => x.Event).Cast<T>();
        }
        /// <summary>
        /// Cast only events of type T to T - will not fail if any are not type T
        /// </summary>
        public static IEnumerable<T> CastFilter<T>(this IEnumerable<IFullEvent> events) where T : IEvent
        {
            return events.Where(x => x.Event is T).Select(x => x.Event).Cast<T>();
        }

        public static Task<IEnumerable<T>> Cast<T>(this Task<IEnumerable<IFullEvent>> events) where T : IEvent
        {
            return events.ContinueWith(x => x.Result.Cast<T>());
        }
        public static Task<IEnumerable<T>> CastFilter<T>(this Task<IEnumerable<IFullEvent>> events) where T : IEvent
        {
            return events.ContinueWith(x => x.Result.CastFilter<T>());
        }

    }
}
