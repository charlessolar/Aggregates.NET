using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    [Versioned("BulkMessage", "Aggregates")]
    public class BulkMessage : Messages.IMessage
    {
        public IFullMessage[] Messages { get; set; }
    }
}
