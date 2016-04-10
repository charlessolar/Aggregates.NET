using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;

namespace Aggregates
{
    public interface IPersistCheckpoints
    {
        Task<Position> Load(String endpoint);

        Task Save(String endpoint, long position);
    }
}