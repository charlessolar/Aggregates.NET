using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface IDomainDead : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
