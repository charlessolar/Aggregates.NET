using Aggregates.Contracts;
using Aggregates.Messages;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Internal;

namespace Aggregates
{
    public static class ContextExtensions
    {
        public static IRepository<T> For<T>(this IMessageHandlerContext context) where T : class, IEntity
        {
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.For<T>();
        }
        public static IPocoRepository<T> Poco<T>(this IMessageHandlerContext context) where T : class, new()
        {
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.Poco<T>();
        }

        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, TService query) where TService : class, IService<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.Service<TService, TResponse>(query, container);
        }
        public static Task<TResponse> Service<TService, TResponse>(this IMessageHandlerContext context, Action<TService> query) where TService : class, IService<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.Service<TService, TResponse>(query, container);
        }

        public static TUnitOfWork App<TUnitOfWork>(this IMessageHandlerContext context) where TUnitOfWork : class, IUnitOfWork
        {
            var uow = context.Extensions.Get<IUnitOfWork>();
            return uow as TUnitOfWork;
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
    }
}
