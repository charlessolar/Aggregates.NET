using System;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface ISurrender : IEvent
    {
        string Endpoint { get; set; }
        Guid Instance { get; set; }
        string Queue { get; set; }
        string CommandType { get; set; }
    }
}
