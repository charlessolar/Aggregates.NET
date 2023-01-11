using Aggregates.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ContextExtensions
    {

        public static TUnitOfWork Uow<TUnitOfWork>(this IMessageHandlerContext context) where TUnitOfWork : class, UnitOfWork.IUnitOfWork
        {
            var uow = context.Extensions.Get<UnitOfWork.IUnitOfWork>();
            return uow as TUnitOfWork;
        }

        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, TService service)
            where TService : class, IService<TResponse>
        {
            var config = context.Extensions.Get<IConfiguration>();
            IProcessor processor;
            if (!context.Extensions.TryGet<IProcessor>(out processor))
                processor = config.ServiceProvider.GetRequiredService<IProcessor>();
            return processor.Process<TService, TResponse>(service, config.ServiceProvider);
        }
        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, Action<TService> service)
            where TService : class, IService<TResponse>
        {
            var config = context.Extensions.Get<IConfiguration>();
            IProcessor processor;
            if (!context.Extensions.TryGet<IProcessor>(out processor))
                processor = config.ServiceProvider.GetRequiredService<IProcessor>();

            return processor.Process<TService, TResponse>(service, config.ServiceProvider);
        }

        public static ISettings GetSettings(this IMessageHandlerContext context)
        {
            context.Extensions.TryGet<ISettings>(out var settings);
            return settings;
        }
    }
}
