using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Attributes;
using NServiceBus;

namespace Shared
{
    public class SayHelloALot : ICommand
    {
        // Specialize the command by the user, 
        // allows us to process all commands from the same user in bulk instead of all commands from many users
        [KeyProperty]
        public String User { get; set; }
        public String Message { get; set; }
    }
}
