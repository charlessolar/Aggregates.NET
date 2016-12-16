using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IMutating
    {
        object Message { get; set; }
        IDictionary<string, string> Headers { get; }
    }
}
