using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates
{
    public interface IProvideService<in TService, TResponse> where TService : IService<TResponse>
    {
        Task<TResponse> Handle(TService query, IServiceContext context);
    }
}
