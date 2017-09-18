using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IProcessor
    {
        Task<TResponse> Process<TQuery, TResponse>(TQuery query, IUnitOfWork uow) where TQuery : IQuery<TResponse>;
    }
}
