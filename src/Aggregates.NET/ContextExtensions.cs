using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates
{
    public static class ContextExtensions
    {

        public static TUnitOfWork App<TUnitOfWork>(this IServiceContext context) where TUnitOfWork : class, UnitOfWork.IApplication
        {
            return context.App as TUnitOfWork;
        }

        public static Task<TResponse> Service<TService, TResponse>(this IServiceContext context, TService service)
            where TService : class, IService<TResponse>
        {
            var container = context.Container;
            var processor = container.Resolve<IProcessor>();
            return processor.Process<TService, TResponse>(service, container);
        }
        public static Task<TResponse> Service<TService, TResponse>(this IServiceContext context, Action<TService> service)
            where TService : class, IService<TResponse>
        {
            var container = context.Container;
            var processor = container.Resolve<IProcessor>();
            var factory = container.Resolve<IEventFactory>();

            return processor.Process<TService, TResponse>(factory.Create(service), container);
        }
    }
}
