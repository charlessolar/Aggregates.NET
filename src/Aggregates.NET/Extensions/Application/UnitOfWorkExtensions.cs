using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Application
{
    public static class UnitOfWorkExtensions
    {
        public static TUnitOfWork Uow<TUnitOfWork>(this IServiceContext context) where TUnitOfWork : UnitOfWork.IApplicationUnitOfWork
        {
            return (TUnitOfWork)context.App ;
        }
    }
}
