using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface EndpointAlive : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
