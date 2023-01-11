using System;

namespace Aggregates
{
    public interface IServiceContext
    {
        UnitOfWork.IDomainUnitOfWork Domain { get; }
        UnitOfWork.IApplicationUnitOfWork App { get; }
        Contracts.IProcessor Processor { get; }

        IServiceProvider Container { get; }
    }
}
