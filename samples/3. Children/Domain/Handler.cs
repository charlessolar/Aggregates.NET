using Aggregates;
using Aggregates.Domain;
using NServiceBus;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    internal class Handler :
        IHandleMessages<NameParent>,
        IHandleMessages<NameChild>
    {
        public async Task Handle(NameParent command, IMessageHandlerContext ctx)
        {
            var entity = await ctx.For<ParentEntity>().TryGet(command.Name);
            if (entity == null)
                entity = await ctx.For<ParentEntity>().New(command.Name);

            entity.Name(command.Name);
        }
        public async Task Handle(NameChild command, IMessageHandlerContext ctx)
        {
            var parent = await ctx.For<ParentEntity>().Get(command.ParentName);

            // Get list of existing children on parent
            var children = await parent.Children<ChildEntity>();
            if (children.Any(x => string.Equals(x.State.Name, command.Name, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException("Children names must be unique");

            // Note:
            // This logic doesn't guarentee distinct children names - the Children projection
            // is eventually consistent so if two NameChild commands are processed at the same 
            // time you could end up not throwing a business exception

            // However because we use the child's name for the id - this makes child names distinct
            // `New` will fail during the second command because the stream already exists
            var child = await parent.For<ChildEntity>().New(command.Name);

            // The only difference is instead of getting a business exception you will see
            // the command fail due to commit errors.

            // Just FYI for when you need uniqueness

            child.Name(command.Name);
        }
    }
}
