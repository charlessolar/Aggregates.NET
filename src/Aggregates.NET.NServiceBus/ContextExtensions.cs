using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Sagas;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ContextExtensions
    {

        public static TUnitOfWork Application<TUnitOfWork>(this IMessageHandlerContext context) where TUnitOfWork : class, UnitOfWork.IApplicationUnitOfWork
        {
            var uow = context.Extensions.Get<UnitOfWork.IApplicationUnitOfWork>();
            return uow as TUnitOfWork;
        }
        public static UnitOfWork.IDomainUnitOfWork Uow(this IMessageHandlerContext context)
        {
            var uow = context.Extensions.Get<UnitOfWork.IDomainUnitOfWork>();
            return uow;
        }

        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, TService service)
            where TService : class, IService<TResponse>
        {
            var provider = context.Extensions.Get<IServiceProvider>();
            IProcessor processor;
            if (!context.Extensions.TryGet<IProcessor>(out processor))
                processor = provider.GetRequiredService<IProcessor>();
            return processor.Process<TService, TResponse>(service, provider);
        }
        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, Action<TService> service)
            where TService : class, IService<TResponse> {
            var provider = context.Extensions.Get<IServiceProvider>();
            IProcessor processor;
            if (!context.Extensions.TryGet<IProcessor>(out processor))
                processor = provider.GetRequiredService<IProcessor>();

            return processor.Process<TService, TResponse>(service, provider);
        }

        public static ISettings GetSettings(this IMessageHandlerContext context)
        {
            context.Extensions.TryGet<ISettings>(out var settings);
            return settings;
        }
        public static IRepository<T> For<T>(this IMessageHandlerContext context) where T : class, IEntity
        {
            var uow = context.Extensions.Get<UnitOfWork.IDomainUnitOfWork>();
            return uow.For<T>();
        }

        public static Task SendToSelf(this IMessageHandlerContext context, Messages.ICommand command)
        {
            var provider = context.Extensions.Get<IServiceProvider>();
            var dispatcher = provider.GetRequiredService<IMessageDispatcher>();

            var message = new FullMessage
            {
                Headers = context.MessageHeaders.Where(x => x.Key != $"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}").ToDictionary(kv => kv.Key, kv => kv.Value),
                Message = command
            };
            _ = Task.Run(() => dispatcher.SendLocal(message), context.CancellationToken);
            return Task.CompletedTask;
        }

        public static CommandSaga Saga(this IMessageHandlerContext context, Id sagaId, string domainDestination = null)
        {
            if (domainDestination == null)
                domainDestination = context.GetSettings()?.CommandDestination;

            if (string.IsNullOrEmpty(domainDestination) && !context.Extensions.TryGet("CommandDestination", out domainDestination))
                throw new ArgumentException("Configuration lacks CommandDestination [Configuration.SetCommandDestination]");
            // Don't know if this is the best way to get the current message
            var currentMessage = (context as IInvokeHandlerContext)?.MessageBeingHandled as Messages.IMessage;

            return new CommandSaga(context, sagaId.ToString(), currentMessage, domainDestination);
        }
    }
}
