using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IConsumeUnitOfWork
    {
        void Start();
        void End(Exception ex = null);
    }
}
