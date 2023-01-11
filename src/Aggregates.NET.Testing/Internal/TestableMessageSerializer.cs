using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aggregates.Internal
{
    internal class TestableMessageSerializer : IMessageSerializer
    {
        public string ContentType => "testing";

        private static Dictionary<Type, object> Serialized = new Dictionary<Type, object>();

        public object[] Deserialize(Stream stream, IList<Type> messageTypes = null)
        {
            return messageTypes.Where(x => Serialized.ContainsKey(x)).Select(x => Serialized[x]).ToArray();
        }

        public void Serialize(object message, Stream stream)
        {
            Serialized[message.GetType()] = message;
        }
    }
}
