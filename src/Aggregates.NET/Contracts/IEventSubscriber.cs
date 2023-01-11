using System;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventSubscriber : IDisposable
    {
        Task Setup(string endpoint, Version version);

        Task Connect();
        Task Shutdown();
    }
}