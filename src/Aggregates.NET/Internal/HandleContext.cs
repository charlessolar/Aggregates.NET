using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class HandleContext : IServiceContext
    {
        public HandleContext(Aggregates.UnitOfWork.IUnitOfWork uow, IProcessor processor, IServiceProvider container)
        {
            BaseUow = uow;
            Processor = processor;
            Container = container;
        }

        public Aggregates.UnitOfWork.IUnitOfWork BaseUow { get; }
        public IProcessor Processor { get; }
        public IServiceProvider Container { get; }

    }
}
