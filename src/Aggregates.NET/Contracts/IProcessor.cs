using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IProcessor
    {
        Task<TResponse> Process<TService, TResponse>(TService service, IServiceProvider container) where TService : IService<TResponse>;
        Task<TResponse> Process<TService, TResponse>(Action<TService> service, IServiceProvider container) where TService : IService<TResponse>;
    }
}
