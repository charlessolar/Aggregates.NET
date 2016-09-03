using Aggregates.Contracts;
using NServiceBus.MessageMutator;
using NServiceBus.ObjectBuilder;
using NServiceBus.UnitOfWork;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IUnitOfWork : IDisposable
    {
        IRepository<T> For<T>() where T : class, IAggregate;
        IEntityRepository<TParent, TParentId, TEntity> For<TParent, TParentId, TEntity>(TParent parent) where TEntity : class, IEntity where TParent : class, IBase<TParentId>;
        IPocoRepository<T> Poco<T>() where T : class, new();
        IPocoRepository<TParent, TParentId, T> Poco<TParent, TParentId, T>(TParent parent) where T : class, new() where TParent : class, IBase<TParentId>;


        Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;
        Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        Task<TResponse> Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>;
        Task<TResponse> Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>;
        
        IBuilder Builder { get; set; }
        Object CurrentMessage { get; }
        IDictionary<String, String> CurrentHeaders { get; }
    }
}