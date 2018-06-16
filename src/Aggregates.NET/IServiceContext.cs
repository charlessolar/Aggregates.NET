using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IServiceContext
    {
        Aggregates.UnitOfWork.IDomain Domain { get; }
        Aggregates.UnitOfWork.IApplication App { get; }
        Aggregates.Contracts.IProcessor Processor { get; }

        IContainer Container { get; }
    }
}
