using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Messages;

namespace Aggregates
{
    public interface IHandleQueries<in TQuery, TResponse> where TQuery : IQuery<TResponse>
    {
        Task<TResponse> Handle(TQuery query, IUnitOfWork uow);
    }
}
