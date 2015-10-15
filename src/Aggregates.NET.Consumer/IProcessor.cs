using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IProcessor
    {
        void Push(Object @event);
        void Push<TEvent>(Action<TEvent> action);
    }
}
