using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IProcessor
    {
        IEnumerable<TResponse> Process<TResponse, TQuery>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        TResponse Compute<TResponse, TComputed>(TComputed compute) where TComputed : IComputed<TResponse>;
    }
}
