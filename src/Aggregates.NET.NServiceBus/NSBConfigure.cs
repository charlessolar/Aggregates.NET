using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates
{
    public static class NSBConfigure
    {
        public static Configure NServiceBus(this Configure config, EndpointConfiguration endpointConfig)
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

            // Todo: have a final .Build() method which does all this stuff so EndpointName is not dependent on ordering
            endpointConfig.MakeInstanceUniquelyAddressable(config.UniqueAddress);

            settings.Set("Retries", config.Retries);
            settings.Set("SlowAlertThreshold", config.SlowAlertThreshold);

            // Set immediate retries to our "MaxRetries" setting
            endpointConfig.Recoverability().Immediate(x =>
            {
                x.NumberOfRetries(config.Retries);
            });

            endpointConfig.Recoverability().Delayed(x =>
            {
                // Delayed retries don't work well with the InMemory context bag storage.  Creating
                // a problem of possible duplicate commits
                x.NumberOfRetries(0);
                //x.TimeIncrease(TimeSpan.FromSeconds(1));
                //x.NumberOfRetries(forever ? int.MaxValue : delayedRetries);
            });

            endpointConfig.EnableFeature<Feature>();

            config.SetupTasks.Add((c) =>
            {
                endpointConfig.LimitMessageProcessingConcurrencyTo(c.ParallelMessages);
                // NSB doesn't have an endpoint name setter other than the constructor, hack it in
                settings.Set("NServiceBus.Routing.EndpointName", c.Endpoint);

                return Aggregates.Bus.Start(endpointConfig);
            });

            return config;
        }

    }
}
