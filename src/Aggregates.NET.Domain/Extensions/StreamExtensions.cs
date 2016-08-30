using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class StreamExtensions
    {
        public static IEnumerable<T> AllEvents<T>(IEventStream stream, Boolean? backwards)
        {
            foreach( var @event in stream.AllEvents(backwards))
            {
                if (@event.Event is T)
                    yield return (T)@event.Event;
            }
        }
        public static IEnumerable<T> OOBEvents<T>(IEventStream stream, Boolean? backwards)
        {
            foreach (var @event in stream.OOBEvents(backwards))
            {
                if (@event.Event is T)
                    yield return (T)@event.Event;
            }
        }
    }
}
