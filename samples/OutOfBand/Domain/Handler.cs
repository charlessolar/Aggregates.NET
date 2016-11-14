using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;
using Aggregates.Attributes;
using Aggregates.Exceptions;
using NServiceBus;
using Shared;

namespace Domain
{
    class Handler :
        IHandleMessages<SetupMarket>,
        IHandleMessages<Trade>
    {
        private readonly IUnitOfWork _uow;

        public Handler(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task Handle(SetupMarket command, IMessageHandlerContext ctx)
        {
            var market = await _uow.For<Market>().New(command.Name);
            market.Setup(command.Bid, command.Ask);
        }
        public async Task Handle(Trade command, IMessageHandlerContext ctx)
        {
            var market = await _uow.For<Market>().Get(command.Market);
            if (market == null)
                throw new BusinessException("Unknown market");
            market.Trade(command.Price, command.Volume);
        }

    }
}
