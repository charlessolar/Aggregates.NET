
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface ISnapshotReader
    {
        Task<ISnapshot> Retreive(string stream);
    }
}
