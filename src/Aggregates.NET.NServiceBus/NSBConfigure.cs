using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NServiceBus.Transport;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;
using NServiceBus.Unicast.Messages;
using NServiceBus.Unicast;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class NSBConfigure
    {
        public static Configure NServiceBus(this Configure config, EndpointConfiguration endpointConfig)
        {
            IStartableEndpointWithExternallyManagedContainer startableEndpoint = null;

            {
                var settings = endpointConfig.GetSettings();
                var conventions = endpointConfig.Conventions();

                // set the configured endpoint name to the one NSB config was constructed with
                config.SetEndpointName(settings.Get<string>("NServiceBus.Routing.EndpointName"));

                conventions.DefiningCommandsAs(type => typeof(Messages.ICommand).IsAssignableFrom(type));
                conventions.DefiningEventsAs(type => typeof(Messages.IEvent).IsAssignableFrom(type));
                conventions.DefiningMessagesAs(type => typeof(Messages.IMessage).IsAssignableFrom(type));

                endpointConfig.AssemblyScanner().ScanAppDomainAssemblies = true;
                endpointConfig.EnableCallbacks();
                endpointConfig.EnableInstallers();

                endpointConfig.UseSerialization<Internal.AggregatesSerializer>();
                endpointConfig.EnableFeature<Feature>();
            }


            config.RegistrationTasks.Add(c =>
            {
                var container = c.Container;

                container.Register(factory => new Aggregates.Internal.DelayedRetry(factory.Resolve<IMetrics>(), factory.Resolve<IMessageDispatcher>()), Lifestyle.Singleton);

                container.Register<IEventMapper>((factory) => new EventMapper(factory.Resolve<IMessageMapper>()), Lifestyle.Singleton);

                container.Register<UnitOfWork.IDomain>((factory) => new NSBUnitOfWork(factory.Resolve<IRepositoryFactory>(), factory.Resolve<IEventFactory>(), factory.Resolve<IVersionRegistrar>()), Lifestyle.UnitOfWork);
                container.Register<IEventFactory>((factory) => new EventFactory(factory.Resolve<IMessageMapper>()), Lifestyle.Singleton);
                container.Register<IMessageDispatcher>((factory) => new Dispatcher(factory.Resolve<IMetrics>(), factory.Resolve<IMessageSerializer>(), factory.Resolve<IEventMapper>(), factory.Resolve<IVersionRegistrar>()), Lifestyle.Singleton);
                container.Register<IMessaging>((factory) => new NServiceBusMessaging(factory.Resolve<MessageHandlerRegistry>(), factory.Resolve<MessageMetadataRegistry>(), factory.Resolve<ReadOnlySettings>()), Lifestyle.Singleton);

                var settings = endpointConfig.GetSettings();

                settings.Set("Retries", config.Retries);
                settings.Set("SlowAlertThreshold", config.SlowAlertThreshold);
                settings.Set("CommandDestination", config.CommandDestination);

                // Set immediate retries to 0 - we handle retries ourselves any message which throws should be sent to error queue
                endpointConfig.Recoverability().Immediate(x =>
                {
                    x.NumberOfRetries(0);
                });

                endpointConfig.Recoverability().Delayed(x =>
                {
                    x.NumberOfRetries(0);
                });


                endpointConfig.MakeInstanceUniquelyAddressable(c.UniqueAddress);
                endpointConfig.LimitMessageProcessingConcurrencyTo(c.ParallelMessages);
                // NSB doesn't have an endpoint name setter other than the constructor, hack it in
                settings.Set("NServiceBus.Routing.EndpointName", c.Endpoint);

                startableEndpoint = EndpointWithExternallyManagedContainer.Create(endpointConfig, new Internal.ContainerAdapter());

                return Task.CompletedTask;
            });

            // Split creating the endpoint and starting the endpoint into 2 seperate jobs for certain (MICROSOFT) DI setup

            config.SetupTasks.Add((c) =>
            {
                return Aggregates.Bus.Start(startableEndpoint);
            });

            return config;
        }

    }
}
