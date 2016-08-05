using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IBase<TId> : IEventSource<TId>, IQueryResponse
    {
    }
}
