using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ContextExtensions
    {

        public static TUnitOfWork Uow<TUnitOfWork>(this IServiceContext context) where TUnitOfWork : class, UnitOfWork.IUnitOfWork
        {
            if (!(context.BaseUow is TUnitOfWork))
                throw new ArgumentException($"Unit of work {context.BaseUow.GetType().Name} is not {typeof(TUnitOfWork).Name}");
            return context.BaseUow as TUnitOfWork;
        }

        public static Task<TResponse> Service<TService, TResponse>(this IServiceContext context, TService service)
            where TService : class, IService<TResponse>
        {
            var container = context.Container;
            return context.Processor.Process<TService, TResponse>(service, container);
        }
        public static Task<TResponse> Service<TService, TResponse>(this IServiceContext context, Action<TService> service)
            where TService : class, IService<TResponse>
        {
            var container = context.Container;
            return context.Processor.Process<TService, TResponse>(service, container);
        }
    }
}
