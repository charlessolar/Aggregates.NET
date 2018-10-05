using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IMessaging
    {
        Type[] GetMessageTypes();
        Type[] GetEntityTypes();
        Type[] GetMessageHierarchy(Type messageType);
    }
}
