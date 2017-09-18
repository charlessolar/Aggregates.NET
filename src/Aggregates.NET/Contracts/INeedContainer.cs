using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    interface INeedContainer
    {
        IContainer Container { get; set; }
    }
}
