using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IInvokeObjects
    {
        Func<Object, Object, IHandleContext, Task> Invoker(object handler, Type messageType);
    }
}
