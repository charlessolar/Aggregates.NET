using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class FullMessage : IFullMessage
    {
        public object Message { get; set; }
        public IDictionary<string,string> Headers { get; set; }
    }
}
