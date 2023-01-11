using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ContextExtensions
    {


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
