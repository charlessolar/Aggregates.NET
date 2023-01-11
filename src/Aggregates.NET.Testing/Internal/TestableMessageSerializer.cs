using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class TestableMessageSerializer : IMessageSerializer
    {
        public string ContentType => "testing";

        public object[] Deserialize(Stream stream, IList<Type> messageTypes = null)
        {
            return new object[] { };
        }

        public void Serialize(object message, Stream stream)
        {
        }
    }
}
