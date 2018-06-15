using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    interface INeedDomainUow
    {
        UnitOfWork.IDomain Uow { get; set; }
    }
}
