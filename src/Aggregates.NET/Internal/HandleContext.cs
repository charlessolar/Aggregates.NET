using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class HandleContext : IHandleContext
    {
        private readonly IDomainUnitOfWork _uow;
        private readonly IUnitOfWork _app;
        private readonly IContainer _container;

        public HandleContext(IDomainUnitOfWork uow, IUnitOfWork app, IContainer container)
        {
            _uow = uow;
            _app = app;
            _container = container;
        }

        public IDomainUnitOfWork UoW => _uow;
        public IUnitOfWork App => _app;
        public IContainer Container => _container;
    }
}
