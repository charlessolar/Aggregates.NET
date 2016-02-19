using Aggregates.Contracts;
using NServiceBus.MessageMutator;
using NServiceBus.UnitOfWork;
using System;
using System.Collections.Generic;
namespace Aggregates
{
    public interface IUnitOfWork : IDisposable, IManageUnitsOfWork, IMutateTransportMessages, IMessageMutator
    {
        IRepository<T> For<T>() where T : class, IAggregate;

        IEnumerable<TResponse> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;
        IEnumerable<TResponse> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        TResponse Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>;
        TResponse Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>;

        void Commit();

        Object CurrentMessage { get; }
    }
}