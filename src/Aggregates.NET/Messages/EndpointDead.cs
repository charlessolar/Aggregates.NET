using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface EndpointDead : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
