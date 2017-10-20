using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    public interface SaidHello : IEvent
    {
        string Message { get; set; }
    }
}
