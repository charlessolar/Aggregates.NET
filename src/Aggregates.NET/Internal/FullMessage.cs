using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Messages;

namespace Aggregates.Internal
{
    class FullMessage : IFullMessage
    {
        public IMessage Message { get; set; }
        public IDictionary<string,string> Headers { get; set; }
    }
}
