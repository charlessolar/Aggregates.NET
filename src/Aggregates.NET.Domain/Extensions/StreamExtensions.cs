using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates.Extensions
{
    public static class StreamExtensions
    {
        public static async Task<IEnumerable<T>> AllEvents<T>(this IEventStream stream, bool? backwards = false) where T : IEvent
        {
            return (await stream.AllEvents(backwards).ConfigureAwait(false)).Where(x => x.Event is T).Select(x => (T)x.Event);
        }
        public static async Task<IEnumerable<T>> OobEvents<T>(this IEventStream stream, bool? backwards = false) where T : IEvent
        {
            return (await stream.OobEvents(backwards).ConfigureAwait(false)).Where(x => x.Event is T).Select(x => (T)x.Event);
        }
    }
}
