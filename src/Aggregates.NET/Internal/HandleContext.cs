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
        public HandleContext(Aggregates.UnitOfWork.IDomain uow, Aggregates.UnitOfWork.IApplication app, IContainer container)
        {
            UoW = uow;
            App = app;
            Container = container;
        }

        public Aggregates.UnitOfWork.IDomain UoW { get; }
        public Aggregates.UnitOfWork.IApplication App { get; }
        public IContainer Container { get; }
    }
}
