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
        public HandleContext(Aggregates.UnitOfWork.IDomain domain, Aggregates.UnitOfWork.IApplication app, IProcessor processor, IContainer container)
        {
            Domain = domain;
            App = app;
            Processor = processor;
            Container = container;
        }

        public Aggregates.UnitOfWork.IDomain Domain { get; }
        public Aggregates.UnitOfWork.IApplication App { get; }
        public IProcessor Processor { get; }
        public IContainer Container { get; }

    }
}
