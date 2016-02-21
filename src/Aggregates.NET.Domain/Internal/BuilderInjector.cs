using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class BuilderInjector : IBehavior<IncomingContext>
    {
        private readonly IBus _bus;

        public BuilderInjector(IBus bus)
        {
            _bus = bus;
        }

        public void Invoke(IncomingContext context, Action next)
        {
            var unitOfWork = context.Builder.Build<IUnitOfWork>();
            unitOfWork.Builder = context.Builder;

            next();
        }
    }

    internal class BuilderInjectorRegistration : RegisterStep
    {
        public BuilderInjectorRegistration()
            : base("BuilderInjector", typeof(BuilderInjector), "Injects builder into unit of work")
        {
            InsertAfter(WellKnownStep.InvokeHandlers);

        }
    }
}
