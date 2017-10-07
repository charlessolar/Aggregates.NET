using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    interface INeedStore
    {
        IStoreEvents Store { get; set; }
        IOobWriter OobWriter { get; set; }
    }
}
