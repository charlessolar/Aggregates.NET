using Aggregates.UnitOfWork;
using System;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface ITrackChildren
    {
        Task Setup(string endpoint, Version version);
        Task<TEntity[]> GetChildren<TEntity, TParent>(IDomainUnitOfWork uow, TParent parent) where TEntity : IChildEntity<TParent> where TParent : IHaveEntities<TParent>;
    }
}
