using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IMessaging
    {
        IEnumerable<Type> GetMessageTypes();
        IEnumerable<Type> GetMessageHierarchy(Type messageType);
    }
}
