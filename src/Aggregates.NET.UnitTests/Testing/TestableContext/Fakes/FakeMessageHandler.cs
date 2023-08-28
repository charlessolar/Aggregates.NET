using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeMessageHandler : 
        IHandleMessages<FakeEvent>, 
        IHandleMessages<FakeCommand> {

        public Task Handle(FakeEvent @event, IMessageHandlerContext context) {
            return Task.CompletedTask;
        }
        public async Task Handle(FakeCommand command, IMessageHandlerContext context) {


            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);
            if (command.RaiseEvent)
                entity.RaiseEvent(command.Content);

        }
        public async Task HandleOther(FakeCommand command, IMessageHandlerContext context) {


            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);
            if (command.RaiseEvent)
                entity.RaiseOtherEvent(command.Content);

        }

        public async Task<long> GetEntityVersion(FakeCommand command, IMessageHandlerContext context) {
            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);

            return entity.Version;
        }

        public async Task<FakeState> GetEntityState(FakeCommand command, IMessageHandlerContext context) {
            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);
            return entity.State;
        }


        public async Task HandleInChild(FakeCommand command, IMessageHandlerContext context) {

            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);
            var child = await entity.For<FakeChildEntity>().Get(command.EntityId);

            if (command.RaiseEvent)
                child.RaiseEvent(command.Content);
        }
        public async Task<long> GetChildEntityVersion(FakeCommand command, IMessageHandlerContext context) {
            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);
            var child = await entity.For<FakeChildEntity>().Get(command.EntityId);

            return child.Version;
        }
        public async Task<FakeChildState> GetChildEntityState(FakeCommand command, IMessageHandlerContext context) {
            var entity = await context.Uow().For<FakeEntity>().Get(command.EntityId);
            var child = await entity.For<FakeChildEntity>().Get(command.EntityId);
            return child.State;
        }
    }
}
