using Aggregates.Contracts;
using NEventStore;
using NEventStore.Dispatcher;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Serialization;

namespace Aggregates.NServiceBus
{
    public static class ConfigureAggregates
    {
        public static void UsingAggregates(this BusConfiguration config, IBuilder builder, IStoreEvents eventStore)
        {
            config.RegisterComponents(c => c.ConfigureComponent<IUnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork));

            config.RegisterComponents(c => c.ConfigureComponent<IStoreEvents>(() => eventStore, DependencyLifecycle.SingleInstance));
            config.RegisterComponents(c => c.ConfigureComponent<IDispatchCommits>(() => builder.Build<IUnitOfWork>(), DependencyLifecycle.InstancePerCall));
        }
    }
}