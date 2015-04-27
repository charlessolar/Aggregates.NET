using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventDescriptor
    {
        String EntityType { get; }

        Int32 Version { get; }
        DateTime Timestamp { get; }
        String EventType { get; }

        IDictionary<String, Object> Headers { get; }
    }
}