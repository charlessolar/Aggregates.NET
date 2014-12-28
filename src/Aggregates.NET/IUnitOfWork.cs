using Aggregates.Contracts;
using NEventStore.Dispatcher;
using NServiceBus.MessageMutator;
using System;
using System.Collections.Generic;
namespace Aggregates
{
    public interface IUnitOfWork : IDisposable, IDispatchCommits, IMutateTransportMessages
    {
        IDictionary<String, String> WorkHeaders { get; }

        IRepository<T> For<T>() where T : class, IEventSource;

        void Commit();
    }
}