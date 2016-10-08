using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IHandleQueries<in TQuery, TResponse> where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
    {
        Task<IEnumerable<TResponse>> Handle(TQuery query);
    }
}
