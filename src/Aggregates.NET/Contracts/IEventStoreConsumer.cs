using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
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

        Task Acknowledge(IFullEvent @event);
        Task Acknowledge(IEnumerable<IFullEvent> events);
        Task<bool> CreateProjection(string name, string definition);
    }
}
