using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class CommandUnitOfWork : IBehavior<IncomingContext>
    {
        public void Invoke(IncomingContext context, Action next)
        {
            var unitOfWorks = context.Builder.BuildAll<ICommandUnitOfWork>();

            foreach (var uow in unitOfWorks)
            {
                uow.Builder = context.Builder;
                uow.Begin();
            }
            try
            {

                next();

            }
            catch (Exception e)
            {
                foreach (var uow in unitOfWorks)
                {
                    uow.End(e);
                }
                throw;
            }
            foreach (var uow in unitOfWorks)
            {
                uow.End();
            }
        }
    }

    internal class CommandUnitOfWorkRegistration : RegisterStep
    {
        public CommandUnitOfWorkRegistration()
            : base("CommandUnitOfWork", typeof(CommandUnitOfWorkRegistration), "Begins and Ends command unit of work")
        {
            InsertAfter(WellKnownStep.ExecuteUnitOfWork);

        }
    }
}
