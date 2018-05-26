using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    public class SayHello : ICommand
    {
        public Guid MessageId { get; set; }
        public string Message { get; set; }
    }
}
