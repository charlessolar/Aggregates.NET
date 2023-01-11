using Aggregates.Contracts;
using Aggregates.Messages;
using System.Collections.Generic;

namespace Aggregates.Internal
{
    class FullMessage : IFullMessage
    {
        public IMessage Message { get; set; }
        public IDictionary<string, string> Headers { get; set; }
    }
}
