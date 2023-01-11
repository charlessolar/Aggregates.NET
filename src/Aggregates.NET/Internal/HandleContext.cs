using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Aggregates.Contracts;
using Aggregates.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class HandleContext : IServiceContext
    {
        public HandleContext(IServiceProvider container)
        {
            Container = container;
        }

        public IDomainUnitOfWork Domain => Container.GetRequiredService<IDomainUnitOfWork>();
        public IApplicationUnitOfWork App => Container.GetRequiredService<IApplicationUnitOfWork>();
        public IProcessor Processor => Container.GetRequiredService<IProcessor>();
        public IServiceProvider Container { get; }

    }
}
