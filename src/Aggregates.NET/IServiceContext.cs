using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IServiceContext
    {
        Aggregates.UnitOfWork.IUnitOfWork BaseUow { get; }
        Aggregates.Contracts.IProcessor Processor { get; }

        IServiceProvider Container { get; }
    }
}
