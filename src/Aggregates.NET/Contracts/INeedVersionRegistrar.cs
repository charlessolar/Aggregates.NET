using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    interface INeedVersionRegistrar
    {
        IVersionRegistrar Registrar { get; set; }
    }
}
