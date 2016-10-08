using System;
using NServiceBus;

namespace Aggregates
{
    public interface IEventSubscriber
    {
        void SubscribeToAll(IMessageSession bus, string endpoint);
        bool ProcessingLive { get; set; }
        Action<string, Exception> Dropped { get; set; }
    }
}