using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface IDomainAlive : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
