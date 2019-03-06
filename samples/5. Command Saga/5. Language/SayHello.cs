using Aggregates;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    [Versioned("SayHello", "Samples")]
    public class SayHello : ICommand
    {
        public string Message { get; set; }
    }
}
