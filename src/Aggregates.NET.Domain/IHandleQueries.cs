using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IHandleQueries<TQuery, TResponse> where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
    {
        Task<IEnumerable<TResponse>> Handle(TQuery query);
    }
}
