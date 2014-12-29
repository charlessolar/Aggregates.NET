using System.Collections.Generic;
using System.Collections.ObjectModel;
using Aggregates.Contracts;
using NEventStore;
using NEventStore.Dispatcher;
using NEventStore.Logging;
using NEventStore.Persistence;
using NEventStore.Serialization;
using NServiceBus.ObjectBuilder;
using NServiceBus;
using StructureMap;

namespace Aggregates
{
    public class AggregatesWireup : Wireup
    {
        private static readonly ILog Logger = LogFactory.BuildLogger(typeof(AggregatesWireup));
        private readonly IContainer _container;

        public AggregatesWireup(Wireup wireup, IContainer container)
            : base(wireup)
        {

            _container = container;

            Logger.Debug("Configuring the store to dispatch messages asynchronously.");
            wireup.UsingAsynchronousDispatchScheduler();
            
        }

        public override IStoreEvents Build()
        {
            var store = base.Build();
            _container.Configure(x => x.For<IStoreEvents>().Use(store));

            return store;
        }
    }

    public static class Extension {

        public static AggregatesWireup UseAggregates(this Wireup wireup, IContainer container)
        {
            return new AggregatesWireup(wireup, container);
        }
    }
    
}