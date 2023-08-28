using Aggregates.Extensions;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeMessageHandler {

        public async Task Handle(FakeEvent @event, IMessageHandlerContext context) {
            if (@event.CreateModel) {
                await context.App().Add(@event.EntityId, new FakeModel { EntityId=@event.EntityId, Content=@event.Content });
            }
            if (@event.DeleteModel) {
                await context.App().Delete< FakeModel>(@event.EntityId);
            }
            if (@event.UpdateModel) {
                await context.App().Update(@event.EntityId, new FakeModel { EntityId = @event.EntityId, Content = @event.Content });
            }
            if (@event.ReadModel) {
                await context.App().Get<FakeModel>(@event.EntityId);
            }
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
