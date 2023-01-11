using System;
using System.Threading.Tasks;

namespace Aggregates.UnitOfWork
{
    public interface IBaseUnitOfWork
    {
        Task Begin();
        Task End(Exception ex = null);
    }
}
