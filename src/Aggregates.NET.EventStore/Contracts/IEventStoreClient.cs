using Aggregates.Internal;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventStoreClient : IDisposable
    {
        public delegate Task EventAppeared(string eventStream, long eventNumber, IFullEvent @event);


        Task<bool> SubscribeToStreamStart(string stream, IEventStoreClient.EventAppeared callback);
        Task<bool> SubscribeToStreamEnd(string stream, IEventStoreClient.EventAppeared callback);

        Task<bool> CreateProjection(string name, string definition);
        Task<bool> EnableProjection(string name);
        Task<bool> ConnectPinnedPersistentSubscription(string stream, string group, EventAppeared callback);
        Task<T> GetProjectionResult<T>(string name, string partition);

        Task<IFullEvent[]> GetEvents(StreamDirection direction, string stream, long? start = null, int? count = null);
        Task<long> WriteEvents(string stream, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null);

        Task WriteMetadata(string stream, int? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null);
        Task<bool> VerifyVersion(string stream, long expectedVersion);
    }
}
