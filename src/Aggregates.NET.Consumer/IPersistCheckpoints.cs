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
        Position Load(String endpoint);

        void Save(String endpoint, long position);
    }
}