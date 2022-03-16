using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Domain
{
    public static class UnitOfWorkExtensions
    {
        public static UnitOfWork.IDomainUnitOfWork Uow(this IServiceContext context)
        {
            return context.Uow<UnitOfWork.IDomainUnitOfWork>();
        }
    }
}
