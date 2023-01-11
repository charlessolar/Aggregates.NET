using System;
using System.Collections.Generic;
using System.IO;

namespace Aggregates.Contracts
{
    public interface IMessageSerializer
    {
        string ContentType { get; }

        void Serialize(object message, Stream stream);

        object[] Deserialize(Stream stream, IList<Type> messageTypes = null);
    }
}
