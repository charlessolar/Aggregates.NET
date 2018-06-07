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

    public interface IAppUnitOfWork
    {
        /// Used to store things you might need persisted should the message be retried
        /// (if you attempt to save 2 objects and 1 fails, you may want to save the successful one here and ignore it when retrying)
        dynamic Bag { get; set; }
    }

    public interface IDomainUnitOfWork 
    {
        IRepository<T> For<T>() where T : IEntity;
        IRepository<TEntity, TParent> For<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IHaveEntities<TParent>;
        IPocoRepository<T> Poco<T>() where T : class, new();
        IPocoRepository<T, TParent> Poco<T, TParent>(TParent parent) where T : class, new() where TParent : class, IHaveEntities<TParent>;

        Guid CommitId { get; }
        object CurrentMessage { get; }
        IDictionary<string, string> CurrentHeaders { get; }
    }
}
