using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventStoreConsumer
    {
        Task<bool> SubscribeToStreamStart(string stream, CancellationToken token, Action<string, long, IFullEvent> callback, Func<Task> disconnected);
        Task<bool> SubscribeToStreamEnd(string stream, CancellationToken token, Action<string, long, IFullEvent> callback, Func<Task> disconnected);
        Task<bool> EnableProjection(string name);

        Task<bool> ConnectPinnedPersistentSubscription(string stream, string group, CancellationToken token, Action<string, long, IFullEvent> callback, Func<Task> disconnected);
        Task<bool> ConnectRoundRobinPersistentSubscription(string stream, string group, CancellationToken token, Action<string, long, IFullEvent> callback, Func<Task> disconnected);

        Task Acknowledge(string stream, long position, IFullEvent @event);
        Task<bool> CreateProjection(string name, string definition);
    }
}
