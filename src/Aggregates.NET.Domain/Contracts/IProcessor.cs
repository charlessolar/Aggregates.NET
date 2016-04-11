using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IProcessor
    {
        Task<IEnumerable<TResponse>> Process<TQuery, TResponse>(IBuilder builder, TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        Task<TResponse> Compute<TComputed, TResponse>(IBuilder builder, TComputed compute) where TComputed : IComputed<TResponse>;
    }
}
