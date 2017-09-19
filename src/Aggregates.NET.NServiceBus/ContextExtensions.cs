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
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IDomainUnitOfWork>().For<T>();
        }
        public static IPocoRepository<T> Poco<T>(this IMessageHandlerContext context) where T : class, new()
        {
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IDomainUnitOfWork>().Poco<T>();
        }

        public static Task<TResponse> Query<TQuery, TResponse>(this IMessageHandlerContext context, TQuery query) where TQuery : IQuery<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IDomainUnitOfWork>().Query<TQuery, TResponse>(query, container.Resolve<IUnitOfWork>());
        }
        public static Task<TResponse> Query<TQuery, TResponse>(this IMessageHandlerContext context, Action<TQuery> query) where TQuery : IQuery<TResponse>
        {
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IDomainUnitOfWork>().Query<TQuery, TResponse>(query, container.Resolve<IUnitOfWork>());
        }
        public static IUnitOfWork UnitOfWork(this IMessageHandlerContext context)
        {
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IUnitOfWork>();
        }
    }
}
