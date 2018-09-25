using System;

namespace Aggregates.Messages
{
    [Versioned("EndpointDead", "Aggregates")]
    public interface EndpointDead : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
