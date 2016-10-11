using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface DomainAlive : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
