using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IServiceContext
    {
        IDomainUnitOfWork UoW { get; }
        IUnitOfWork App { get; }

        IContainer Container { get; }
    }
}
