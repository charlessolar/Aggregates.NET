using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IMutating
    {
        object Message { get; set; }
        IDictionary<string, string> Headers { get; }
    }
}
