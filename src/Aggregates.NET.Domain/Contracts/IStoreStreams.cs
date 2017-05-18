using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IStoreStreams
    {
        Task<IEventStream> GetStream<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource;
        Task<IEventStream> NewStream<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource;

        Task<long> GetSize<T>(IEventStream stream, string oob) where T : class, IEventSource;
        Task<IEnumerable<IFullEvent>> GetEvents<T>(IEventStream stream, long start, int count, string oob = null) where T : class, IEventSource;
        Task<IEnumerable<IFullEvent>> GetEventsBackwards<T>(IEventStream stream, long start , int count, string oob = null) where T : class, IEventSource;

        Task WriteStream<T>(Guid commitId, IEventStream stream, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
        Task VerifyVersion<T>(IEventStream stream) where T : class, IEventSource;

        Task Freeze<T>(IEventStream stream) where T : class, IEventSource;
        Task Unfreeze<T>(IEventStream stream) where T : class, IEventSource;
    }
}