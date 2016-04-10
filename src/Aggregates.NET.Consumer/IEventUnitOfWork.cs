using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IEventUnitOfWork
    {
        IBuilder Builder { get; set; }

        Task Begin();
        Task End(Exception ex = null);
    }
}
