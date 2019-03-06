using Aggregates;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    [Versioned("Echo", "Samples")]
    public class Echo : ICommand
    {
        public string Message { get; set; }
    }
}
