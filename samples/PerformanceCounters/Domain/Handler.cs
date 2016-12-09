using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;
using NServiceBus;
using Shared;

namespace Domain
{
    class Handler : IHandleMessages<Command>
    {
        private readonly IUnitOfWork _uow;

        public Handler(IUnitOfWork uow)
        {
            _uow = uow;    
        }

        public async Task Handle(Command command, IMessageHandlerContext ctx)
        {
            var user = await _uow.For<User>().TryGet(command.User);
            if (user == null)
            {
                user = await _uow.For<User>().New(command.User);
                user.Create();
            }
            user.SayHello(command.Message);
        }
    }
}
