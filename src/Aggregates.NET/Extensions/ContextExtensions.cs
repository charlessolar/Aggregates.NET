using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Extensions
{
    public static class ContextExtensions
    {

        public static TUnitOfWork App<TUnitOfWork>(this IHandleContext context) where TUnitOfWork : class, IUnitOfWork
        {
            return context.App as TUnitOfWork;
        }
    }
}
