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

namespace Aggregates
{
    public static class NSBConfigure
    {
        public static Configure NServiceBus(this Configure config, EndpointConfiguration endpointConfig)
        {
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

                return Task.CompletedTask;
            });

            config.SetupTasks.Add((c) =>
            {
                var settings = endpointConfig.GetSettings();

                settings.Set("Retries", config.Retries);
                settings.Set("SlowAlertThreshold", config.SlowAlertThreshold);

                if (!c.Passive)
                {
                    // Set immediate retries to 0 - we handle retries ourselves any message which throws should be sent to error queue
                    endpointConfig.Recoverability().Immediate(x =>
                    {
                        x.NumberOfRetries(0);
                    });

                    endpointConfig.Recoverability().Delayed(x =>
                    {
                        x.NumberOfRetries(0);
                    });
                }

                endpointConfig.MakeInstanceUniquelyAddressable(c.UniqueAddress);
                endpointConfig.LimitMessageProcessingConcurrencyTo(c.ParallelMessages);
                // NSB doesn't have an endpoint name setter other than the constructor, hack it in
                settings.Set("NServiceBus.Routing.EndpointName", c.Endpoint);

                return Aggregates.Bus.Start(endpointConfig);
            });

            return config;
        }

    }
}
