using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IProcessor
    {
        Task<TResponse> Process<TQuery, TResponse>(TQuery query, IContainer container) where TQuery : IQuery<TResponse>;
    }
}
