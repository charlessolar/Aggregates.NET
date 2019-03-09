using Aggregates;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    [Versioned("SaidHello", "Language")]
    public interface SaidHello : IEvent
    {
        Guid MessageId { get; set; }
        string Message { get; set; }
    }
}
