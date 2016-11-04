using System;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates
{
    public interface IEventSubscriber
    {
        Task Setup(string endpoint, int readsize);

        Task Subscribe();
        bool ProcessingLive { get; set; }
        Action<string, Exception> Dropped { get; set; }
    }
}