using NServiceBus;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IIncomingContextAccessor
    {
        String PhysicalMessageId { get; }
        byte[] PhysicalMessageBody { get; }
        MessageIntentEnum PhysicalMessageMessageIntent { get; }

        void SetPhysicalMessageHeader(String key, String value);
        IDictionary<String, String> PhysicalMessageHeaders { get; }
        void Set<T>(String name, T value);
        void Set<T>(T value);
        T Get<T>(String name);
        T Get<T>();
        Boolean TryGet<T>(String name, out T value);
        Boolean TryGet<T>(out T value);

        object MessageHandlerInstance { get; }

        Boolean HandlerInvocationAborted { get; }

        object IncomingLogicalMessageInstance { get; }
        Type IncomingLogicalMessageMessageType { get; }

        IBuilder Builder { get; }
    }
}
