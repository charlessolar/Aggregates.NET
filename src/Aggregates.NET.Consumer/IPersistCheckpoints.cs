using System.Threading.Tasks;
using EventStore.ClientAPI;

namespace Aggregates
{
    public interface IPersistCheckpoints
    {
        Task<Position> Load(string endpoint);

        Task Save(string endpoint, long position);
    }
}