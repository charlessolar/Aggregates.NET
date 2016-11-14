using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public class SetupMarket : ICommand
    {
        public string Name { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
    }
}
