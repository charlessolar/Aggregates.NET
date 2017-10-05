using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Language
{
    public class SayHello : ICommand
    {
        public string Message { get; set; }
    }
}
