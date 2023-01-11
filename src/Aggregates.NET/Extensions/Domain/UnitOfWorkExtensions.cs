using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Domain
{
    public static class UnitOfWorkExtensions
    {
        public static IRepository<T> For<T>(this IServiceContext context) where T : class, IEntity
        {
            return context.Domain.For<T>();
        }
    }
}
