using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;
using Aggregates.Exceptions;
using Shared;

namespace Domain
{
    class Market : Aggregates.Aggregate<Market>
    {
        private Wall Wall;

        private Market()
        {
        }

        public void Setup(decimal bid, decimal ask)
        {
            if (bid >= ask)
                throw new BusinessException("Bid must be smaller than ask");

            Apply<MarketCreated>(x =>
            {
                x.Market = this.Id;
                x.Bid = bid;
                x.Ask = ask;
            });
        }

        private void Handle(MarketCreated e)
        {
            this.Wall = new Wall(e.Bid, e.Ask);
            DefineOob("trades", transient: true);
        }

        public void Trade(decimal price, decimal amount)
        {
            if (price > Wall.Ask || price < Wall.Bid)
                throw new BusinessException("Invalid trade - price is not within Bid/Ask");

            // Raise is the method that saves OOB events - which will not be hydrated in the future
            // This event is not important to business logic so its unnecessary to hydrate
            Raise<TradeRecorded>(x =>
            {
                x.Market = this.Id;
                x.Price = price;
                x.Amount = amount;
            }, "trades");
        }
        
    }
}
