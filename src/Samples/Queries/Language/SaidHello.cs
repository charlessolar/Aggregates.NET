using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    public interface SaidHello : IEvent
    {
        Guid MessageId { get; set; }
        string Message { get; set; }
    }
}
