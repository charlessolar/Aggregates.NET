using Aggregates;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    [Versioned("SaidHello", "Samples")]
    public interface SaidHello : IEvent
    {
        string Message { get; set; }
    }
}
