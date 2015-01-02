using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IRouteResolver
    {
        Action<Object> Resolve<TId>(Aggregate<TId> aggregate, Type eventType);
    }
}
