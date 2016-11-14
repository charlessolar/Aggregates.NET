using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public class Trade : ICommand
    {
        public string Market { get; set; }
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
    }
}
