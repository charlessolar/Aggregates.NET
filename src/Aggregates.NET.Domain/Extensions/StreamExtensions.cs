using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class StreamExtensions
    {
        public static async Task<IEnumerable<T>> AllEvents<T>(this IEventStream stream, Boolean? backwards = false) where T : IEvent
        {
            return (await stream.AllEvents(backwards)).Where(x => x.Event is T).Select(x => (T)x.Event);
        }
        public static async Task<IEnumerable<T>> OOBEvents<T>(this IEventStream stream, Boolean? backwards = false) where T : IEvent
        {
            return (await stream.OOBEvents(backwards)).Where(x => x.Event is T).Select(x => (T)x.Event);
        }
    }
}
