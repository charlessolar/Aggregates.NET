using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;
using Aggregates.Attributes;
using NServiceBus;
using Shared;

namespace Domain
{
    [Delayed(typeof(SayHelloALot), Count: 1000)]
    class Handler : 
        IHandleMessages<SayHello>, 
        IHandleMessages<SayHelloALot>,
        IHandleMessages<Start>,
        IHandleMessages<End>
    {
        private static int Processed = 0;

        private readonly IUnitOfWork _uow;

        public Handler(IUnitOfWork uow)
        {
            _uow = uow;    
        }

        public async Task Handle(SayHello command, IMessageHandlerContext ctx)
        {
            Processed++;
            var user = await _uow.For<User>().TryGet(command.User);
            if (user == null)
            {
                user = await _uow.For<User>().New(command.User);
                user.Create();
            }
            user.SayHello(command.Message);
        }
        public async Task Handle(SayHelloALot command, IMessageHandlerContext ctx)
        {
            Processed++;
            var user = await _uow.For<User>().TryGet(command.User);
            if (user == null)
            {
                user = await _uow.For<User>().New(command.User);
                user.Create();
            }
            user.SayHelloALot(command.Message);
        }

        public async Task Handle(Start command, IMessageHandlerContext ctx)
        {
            var a = await _uow.Poco<Time>().TryGet("time");
            if (a == null)
            {
                a = await _uow.Poco<Time>().New("time");
            }
            a.Start = DateTime.UtcNow;
        }
        public async Task Handle(End command, IMessageHandlerContext ctx)
        {
            var a = await _uow.Poco<Time>().Get("time");

            var time = DateTime.UtcNow - a.Start.Value;
            Console.WriteLine($"-- Processing {Processed} commands took {time.TotalMilliseconds} --");

            a.Start = null;
            Processed = 0;
        }
    }
}
