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

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ContextExtensions
    {
        public static IRepository<T> For<T>(this IMessageHandlerContext context) where T : class, IEntity
        {
            var uow = context.Extensions.Get<UnitOfWork.IDomain>();
            return uow.For<T>();
        }
        
        public static TUnitOfWork App<TUnitOfWork>(this IMessageHandlerContext context) where TUnitOfWork : class, UnitOfWork.IApplication
        {
            var uow = context.Extensions.Get<UnitOfWork.IApplication>();
            return uow as TUnitOfWork;
        }
        /// <summary>
        /// Easier access to uow if user implements IGeneric
        /// </summary>
        public static UnitOfWork.IGeneric UoW(this IMessageHandlerContext context)
        {
            var uow = context.Extensions.Get<UnitOfWork.IApplication>();
            return uow as UnitOfWork.IGeneric;
        }
        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, TService service)
            where TService : class, IService<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            IProcessor processor;
            if(!context.Extensions.TryGet<IProcessor>(out processor))
                processor = container.Resolve<IProcessor>();
            return processor.Process<TService, TResponse>(service, container);
        }
        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, Action<TService> service)
            where TService : class, IService<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            IProcessor processor;
            if (!context.Extensions.TryGet<IProcessor>(out processor))
                processor = container.Resolve<IProcessor>();

            return processor.Process<TService, TResponse>(service, container);
        }

        public static Task SendToSelf(this IMessageHandlerContext context, Messages.ICommand command)
        {
            var container = context.Extensions.Get<IContainer>();
            var dispatcher = container.Resolve<IMessageDispatcher>();

            var message = new FullMessage
            {
                Headers = context.MessageHeaders.Where(x => x.Key != $"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}").ToDictionary(kv => kv.Key, kv => kv.Value),
                Message = command
            };
            Task.Run(() => dispatcher.SendLocal(message));
            return Task.CompletedTask;
        }

        public static CommandSaga Saga(this IMessageHandlerContext context, Id sagaId, string domainDestination = null)
        {
            if (string.IsNullOrEmpty(domainDestination) && !context.Extensions.TryGet("CommandDestination", out domainDestination))
                throw new ArgumentException("Configuration lacks CommandDestination [Configuration.SetCommandDestination]");
            // Don't know if this is the best way to get the current message
            var currentMessage = (context as IInvokeHandlerContext)?.MessageBeingHandled as Messages.IMessage;

            return new CommandSaga(context, sagaId.ToString(), currentMessage, domainDestination);
        }
    }
}
