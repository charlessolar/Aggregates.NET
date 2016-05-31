using Aggregates.Contracts;
using NServiceBus.MessageMutator;
using NServiceBus.ObjectBuilder;
using NServiceBus.UnitOfWork;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IUnitOfWork : IDisposable,  IMutateTransportMessages, IMessageMutator
    {
        IRepository<T> For<T>() where T : class, IAggregate;
        IEntityRepository<TAggregateId, TEntity> For<TAggregateId, TEntity>(IEntity<TAggregateId> parent) where TEntity : class, IEntity;

        Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;
        Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        Task<TResponse> Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>;
        Task<TResponse> Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>;
        
        IBuilder Builder { get; set; }
        Object CurrentMessage { get; }
    }
}