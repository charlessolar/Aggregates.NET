using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IRouteResolver
    {
        Action<IEventSource, Object> Resolve(IEventSource eventsource, Type eventType);
        Action<IEventSource, Object> Conflict(IEventSource eventsource, Type eventType);
    }
}
