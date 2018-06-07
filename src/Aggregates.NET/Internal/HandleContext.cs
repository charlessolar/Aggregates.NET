using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class HandleContext : IServiceContext
    {
        private readonly IDomainUnitOfWork _uow;
        private readonly IAppUnitOfWork _app;
        private readonly IContainer _container;

        public HandleContext(IDomainUnitOfWork uow, IAppUnitOfWork app, IContainer container)
        {
            _uow = uow;
            _app = app;
            _container = container;
        }

        public IDomainUnitOfWork UoW => _uow;
        public IAppUnitOfWork App => _app;
        public IContainer Container => _container;
    }
}
