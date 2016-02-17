using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public class Feature : NServiceBus.Features.Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<ExceptionFilter>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<QueryProcessor>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<Func<Accept>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return () => { return eventFactory.CreateInstance<Accept>(); };
            }, DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<Func<String, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (message) => { return eventFactory.CreateInstance<Reject>(e => { e.Message = message; }); };
            }, DependencyLifecycle.InstancePerCall);

            context.Pipeline.Register<ExceptionFilterRegistration>();
        }
    }

    public class Dispatcher : IDispatcher
    {
        protected readonly IBus _bus;

        public Dispatcher(IBus bus)
        {
            _bus = bus;
        }

        public void Dispatch(IWritableEvent @event)
        {
            _bus.OutgoingHeaders.Merge(@event.Descriptor.ToDictionary());
            _bus.Publish(@event.Event);
        }
    }
}