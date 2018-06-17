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

        public static TUnitOfWork App<TUnitOfWork>(this IServiceContext context) where TUnitOfWork : class, UnitOfWork.IApplication
        {
            return context.App as TUnitOfWork;
        }
        /// <summary>
        /// Easier access to uow if user implements IGeneric
        /// </summary>
        public static UnitOfWork.IGeneric App(this IServiceContext context)
        {
            return context.App as UnitOfWork.IGeneric;
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
