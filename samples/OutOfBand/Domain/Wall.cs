using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;

namespace Domain
{
    class Wall : ValueObject<Wall>
    {
        public Wall(decimal bid, decimal ask)
        {
            this.Bid = bid;
            this.Ask = ask;
        }

        public decimal Bid { get; private set; }
        public decimal Ask { get; private set; }
    }
}
