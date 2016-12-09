using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public class Command : ICommand
    {
        public Guid Guid { get; set; }
    }
}
