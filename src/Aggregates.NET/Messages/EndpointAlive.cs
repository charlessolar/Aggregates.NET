using System;

namespace Aggregates.Messages
{
    [Versioned("EndpointAlive", "Aggregates")]
    public interface EndpointAlive : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
