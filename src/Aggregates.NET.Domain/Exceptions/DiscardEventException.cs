using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    /// <summary>
    /// Used by conflict resolvers to indicate that the event should just be discarded, not merged into the stream
    /// </summary>
    public class DiscardEventException : Exception
    {
        public DiscardEventException() { }
        public DiscardEventException(String Message) : base(Message) { }
    }
}
