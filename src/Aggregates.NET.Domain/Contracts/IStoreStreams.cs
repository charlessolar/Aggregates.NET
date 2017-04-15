using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IStoreStreams
    {
        Task Evict<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource;
        Task Cache<T>(IEventStream stream) where T : class, IEventSource;

        Task<IEventStream> GetStream<T>(string bucket, Id streamId, IEnumerable<Id> parents = null, ISnapshot snapshot = null) where T : class, IEventSource;
        Task<IEventStream> NewStream<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> GetEvents<T>(string bucket, Id streamId, IEnumerable<Id> parents = null, long? start = null, int? count = null) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> GetEventsBackwards<T>(string bucket, Id streamId, IEnumerable<Id> parents = null, long? start = null, int? count = null) where T : class, IEventSource;

        Task WriteStream<T>(IEventStream stream, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
        Task VerifyVersion<T>(IEventStream stream) where T : class, IEventSource;

        Task Freeze<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource;
        Task Unfreeze<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource;

        IBuilder Builder { get; set; }
    }
}