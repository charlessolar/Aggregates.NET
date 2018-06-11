using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class HandleContext : IServiceContext
    {
        public HandleContext(IDomainUnitOfWork uow, IAppUnitOfWork app, IContainer container)
        {
            UoW = uow;
            App = app;
            Container = container;
        }

        public IDomainUnitOfWork UoW { get; }
        public IAppUnitOfWork App { get; }
        public IContainer Container { get; }
    }
}
