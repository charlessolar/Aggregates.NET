using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IStoreEvents
    {
        Task Evict<T>(string bucket, string streamId) where T : class, IEventSource;
        Task Cache<T>(IEventStream stream) where T : class, IEventSource;

        Task<IEventStream> GetStream<T>(string bucket, string streamId, ISnapshot snapshot = null) where T : class, IEventSource;
        Task<IEventStream> NewStream<T>(string bucket, string streamId) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> GetEvents<T>(string bucket, string streamId, int? start = null, int? count = null) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> GetEventsBackwards<T>(string bucket, string streamId, int? start = null, int? count = null) where T : class, IEventSource;

        Task WriteEvents<T>(string bucket, string streamId, int expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource;

        Task AppendEvents<T>(string bucket, string streamId, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource;

        Task WriteEventMetadata<T>(string bucket, string streamId, int? maxCount = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null) where T : class, IEventSource;

        IBuilder Builder { get; set; }
    }
}