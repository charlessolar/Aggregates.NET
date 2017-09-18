using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Extensions
{
    public static class ContextExtensions
    {
        public static IDomainUnitOfWork Entities(this IMessageHandlerContext context)
        {
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IDomainUnitOfWork>();
        }
        public static IUnitOfWork UnitOfWork(this IMessageHandlerContext context)
        {
            var container = context.Extensions.Get<IContainer>();
            return container.Resolve<IUnitOfWork>();
        }
    }
}
