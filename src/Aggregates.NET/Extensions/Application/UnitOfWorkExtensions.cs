using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Application
{
    public static class UnitOfWorkExtensions
    {
        public static UnitOfWork.IApplicationUnitOfWork Uow(this IServiceContext context)
        {
            return context.Uow<UnitOfWork.IApplicationUnitOfWork>();
        }
    }
}
