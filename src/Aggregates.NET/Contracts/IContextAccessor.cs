using NServiceBus;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IContextAccessor
    {
        String PhysicalMessageId { get; }
        byte[] PhysicalMessageBody { get; }
        MessageIntentEnum PhysicalMessageMessageIntent { get; }

        void SetPhysicalMessageHeader(String key, String value);
        IDictionary<String, String> PhysicalMessageHeaders { get; }
        void Set<T>(String name, T value);

        object IncomingLogicalMessageInstance { get; }
        Type IncomingLogicalMessageMessageType { get; }

        IBuilder Builder { get; }
    }
}
