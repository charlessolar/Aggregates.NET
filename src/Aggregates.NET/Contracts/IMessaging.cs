using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IMessaging
    {
        IEnumerable<Type> GetMessageTypes();
        IEnumerable<Type> GetMessageHierarchy(Type messageType);
    }
}
