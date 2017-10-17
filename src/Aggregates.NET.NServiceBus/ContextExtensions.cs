using Aggregates.Contracts;
using Aggregates.Messages;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class ContextExtensions
    {
        public static IRepository<T> For<T>(this IMessageHandlerContext context) where T : IEntity
        {
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.For<T>();
        }
        public static IPocoRepository<T> Poco<T>(this IMessageHandlerContext context) where T : class, new()
        {
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.Poco<T>();
        }

        public static Task<TResponse> Query<TQuery, TResponse>(this IMessageHandlerContext context, TQuery query) where TQuery : IQuery<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.Query<TQuery, TResponse>(query, container);
        }
        public static Task<TResponse> Query<TQuery, TResponse>(this IMessageHandlerContext context, Action<TQuery> query) where TQuery : IQuery<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            var uow = context.Extensions.Get<IDomainUnitOfWork>();
            return uow.Query<TQuery, TResponse>(query, container);
        }

        public static TUnitOfWork App<TUnitOfWork>(this IMessageHandlerContext context) where TUnitOfWork : class, IUnitOfWork
        {
            var uow = context.Extensions.Get<IUnitOfWork>();
            return uow as TUnitOfWork;
        }
    }
}
