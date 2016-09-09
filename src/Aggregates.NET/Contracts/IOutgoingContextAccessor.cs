using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IOutgoingContextAccessor
    {
        object OutgoingLogicalMessageInstance { get; }
        Type OutgoingLogicalMessageMessageType { get; }

        void UpdateMessageInstance(object msg);

        void Set<T>(String name, T value);
        void Set<T>(T value);
        T Get<T>(String name);
        T Get<T>();
        Boolean TryGet<T>(String name, out T value);
        Boolean TryGet<T>(out T value);

        IBuilder Builder { get; }
    }
}
