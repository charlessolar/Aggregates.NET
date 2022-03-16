using Aggregates.Contracts;
using Aggregates.Messages;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Internal;
using System.Diagnostics.CodeAnalysis;
using Aggregates.Sagas;
using NServiceBus.Pipeline;
using Microsoft.Extensions.DependencyInjection;

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
            if(!context.Extensions.TryGet<IProcessor>(out processor))
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
            var settings = context.Extensions.Get<ISettings>();
            return settings;
        }
    }
}
