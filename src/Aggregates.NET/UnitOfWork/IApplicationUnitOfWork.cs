using Aggregates.UnitOfWork.Query;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.UnitOfWork
{
    /// <summary>
    /// A generic repository which integrates with Testing package
    /// do not need to implement yourself - but recomended if you want 
    /// easier testing!
    /// </summary>
    public interface IApplicationUnitOfWork : IUnitOfWork
    {
        Task Add<T>(Id id, T document) where T : class;
        Task Update<T>(Id id, T document) where T : class;

        Task<T> Get<T>(Id id) where T : class;
        Task<T> TryGet<T>(Id id) where T : class;

        Task Delete<T>(Id id) where T : class;

        Task<IQueryResult<T>> Query<T>(IDefinition query) where T : class;
    }
}
