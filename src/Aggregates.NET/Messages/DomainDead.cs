using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface DomainDead : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
