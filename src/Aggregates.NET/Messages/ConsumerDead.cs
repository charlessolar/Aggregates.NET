using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface IConsumerDead : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
