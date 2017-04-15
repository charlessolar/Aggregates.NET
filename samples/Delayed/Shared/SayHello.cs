using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Attributes;
using NServiceBus;

namespace Shared
{
    public class SayHello : ICommand
    {
        public String User { get; set; }
        public String Message { get; set; }
    }
}
