using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IQueryProcessor
    {
        IEnumerable<TResponse> Process<TResponse, TQuery>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;
    }
}
