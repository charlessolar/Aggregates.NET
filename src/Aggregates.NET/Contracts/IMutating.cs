using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IMutating
    {
        object Message { get; set; }
        IDictionary<string, string> Headers { get; }
    }
}
