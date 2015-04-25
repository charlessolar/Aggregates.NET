using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface INeedStream
    {
        String StreamId { get; set; }
        Int32 StreamVersion { get; set; }
    }
}