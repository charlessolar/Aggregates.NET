using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IProcessor
    {
        Task<IEnumerable<TResponse>> Process<TQuery, TResponse>(IBuilder builder, TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        Task<TResponse> Compute<TComputed, TResponse>(IBuilder builder, TComputed compute) where TComputed : IComputed<TResponse>;
    }
}
