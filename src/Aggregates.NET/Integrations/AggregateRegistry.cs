using Aggregates.Contracts;
using Aggregates.Internal;
using NEventStore;
using NEventStore.Dispatcher;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Serialization;
using StructureMap.Configuration.DSL;

namespace Aggregates
{
    public class AggregateRegistry : Registry
    {
        public AggregateRegistry()
        {
            For<IUnitOfWork>().Use<UnitOfWork>().Singleton();
            For<IRouteResolver>().Use<DefaultRouteResolver>();
        }
    }
}