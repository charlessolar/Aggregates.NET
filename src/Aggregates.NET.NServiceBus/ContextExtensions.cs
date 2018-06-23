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

        /// <summary>
        /// Allows you to run a series of tasks using the full bus - Request/Response available
        /// </summary>
        public static Task LocalSaga(this IMessageHandlerContext context, Func<IMessageSession, Task> saga)
        {
            Task.Run(() => saga(Bus.Instance));
            return Task.CompletedTask;
        }
    }
}
