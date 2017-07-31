using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IUnitOfWork
    {
        IRepository<T> For<T>() where T : State<T>;
        IRepository<TParent, TEntity> For<TParent, TEntity>(TParent parent) where TEntity : State<TEntity, TParent> where TParent : State<TParent>;
        IPocoRepository<T> Poco<T>() where T : class, new();
        IPocoRepository<TParent, T> Poco<TParent, T>(TParent parent) where T : class, new() where TParent : State<TParent>;
    }
}
