using Aggregates.Contracts;
using NEventStore.Dispatcher;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
namespace Aggregates
{
    public interface IUnitOfWork : IDisposable, IDispatchCommits, IBehavior<IncomingContext>, IBehavior<OutgoingContext>
    {
        IRepository<T> For<T>() where T : class, IEventSourceBase;

        void Commit();
    }
}