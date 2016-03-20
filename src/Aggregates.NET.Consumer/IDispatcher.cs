using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IDispatcher
    {
        void Process(Object @event, IEventDescriptor descriptor = null, long? position = null, int? delayed = null);
        void Dispatch<TEvent>(Action<TEvent> action);
    }
}