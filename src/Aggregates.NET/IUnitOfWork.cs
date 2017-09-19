using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Messages;

namespace Aggregates
{
    public interface IUnitOfWork
    {
        Task Begin();
        Task End(Exception ex = null);
    }

    public interface IDomainUnitOfWork
    {
        Task Begin();
        Task End(Exception ex = null);

        IRepository<T> For<T>() where T : IEntity;
        IRepository<TParent, TEntity> For<TParent, TEntity>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IEntity;
        IPocoRepository<T> Poco<T>() where T : class, new();
        IPocoRepository<TParent, T> Poco<TParent, T>(TParent parent) where T : class, new() where TParent : IEntity;


        Task<TResponse> Query<TQuery, TResponse>(TQuery query, IUnitOfWork uow) where TQuery : IQuery<TResponse>;
        Task<TResponse> Query<TQuery, TResponse>(Action<TQuery> query, IUnitOfWork uow) where TQuery : IQuery<TResponse>;

        Guid CommitId { get; }
        object CurrentMessage { get; }
        IDictionary<string, string> CurrentHeaders { get; }
    }
}
