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
    [Delayed(typeof(SayHelloALot), count: 1000)]
    class Handler : 
        IHandleMessages<UserSignIn>,
        IHandleMessages<SayHello>, 
        IHandleMessages<SayHelloALot>,
        IHandleMessages<StartHello>,
        IHandleMessages<EndHello>
    {
        private static int Processed = 0;

        private readonly IUnitOfWork _uow;

        public Handler(IUnitOfWork uow)
        {
            _uow = uow;    
        }

        public async Task Handle(UserSignIn command, IMessageHandlerContext ctx)
        {

            var user = await _uow.For<User>().New(command.User);
            user.Create();
        }

        public async Task Handle(SayHello command, IMessageHandlerContext ctx)
        {
            Processed++;
            var user = await _uow.For<User>().Get(command.User);
            user.SayHello(command.Message);
        }
        public async Task Handle(SayHelloALot command, IMessageHandlerContext ctx)
        {
            Processed++;
            var user = await _uow.For<User>().Get(command.User);
            user.SayHelloALot(command.Message);
        }

        public async Task Handle(StartHello command, IMessageHandlerContext ctx)
        {
            var user = await _uow.For<User>().Get(command.User);

            user.StartHello(command.Timestamp);

            var a = await _uow.Poco<Time>().TryGet("time");
            if (a == null)
            {
                a = await _uow.Poco<Time>().New("time");
            }
            a.Start = DateTime.UtcNow;
        }
        public async Task Handle(EndHello command, IMessageHandlerContext ctx)
        {
            var user = await _uow.For<User>().Get(command.User);

            user.EndHello(command.Timestamp);

            var a = await _uow.Poco<Time>().Get("time");

            var time = DateTime.UtcNow - a.Start.Value;
            Console.WriteLine($"-- Processing {Processed} commands took {time.TotalMilliseconds} --");

            a.Start = null;
            Processed = 0;
        }
    }
}
