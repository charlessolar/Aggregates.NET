using System;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventSubscriber : IDisposable
    {
        Task Setup(string endpoint, CancellationToken cancelToken);

        Task Connect();
    }
}