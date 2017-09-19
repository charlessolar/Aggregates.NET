using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    interface INeedDomainUow
    {
        IDomainUnitOfWork Uow { get; set; }
    }
}
