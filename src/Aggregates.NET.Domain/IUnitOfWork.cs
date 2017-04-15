using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public interface IUnitOfWork : IEventMutator, ICommandMutator, IDisposable
    {
        IRepository<T> For<T>() where T : class, IAggregate;
        IRepository<TParent, TEntity> For<TParent, TEntity>(TParent parent) where TEntity : class, IEntity where TParent : class, IBase;
        IPocoRepository<T> Poco<T>() where T : class, new();
        IPocoRepository<TParent, T> Poco<TParent, T>(TParent parent) where T : class, new() where TParent : class, IBase;


        Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;
        Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>;

        Task<TResponse> Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>;
        Task<TResponse> Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>;
        
        IBuilder Builder { get; set; }
        Guid CommitId { get; }
        object CurrentMessage { get; }
        IDictionary<string, string> CurrentHeaders { get; }
    }
}