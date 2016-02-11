using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IService : IDisposable
    {
        void Begin();
        void End(Exception ex = null);
    }
}
