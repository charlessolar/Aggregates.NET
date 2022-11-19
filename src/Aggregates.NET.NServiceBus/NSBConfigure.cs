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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Aggregates.Extensions;
using Aggregates.Messages;
using NServiceBus.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class NSBConfigure
    {
        public static Settings NServiceBus(this Settings config, EndpointConfiguration endpointConfig)
        {
            IStartableEndpointWithExternallyManagedContainer startableEndpoint = null;

            {
                var settings = endpointConfig.GetSettings();
                var conventions = endpointConfig.Conventions();

                settings.Set(NSBDefaults.AggregatesSettings, config);
                settings.Set(NSBDefaults.AggregatesConfiguration, config.Configuration);

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


            Settings.RegistrationTasks.Add((container, settings) =>
            {

                container.AddTransient<IEventMapper, EventMapper>();

                container.Replace(ServiceDescriptor.Scoped<UnitOfWork.IDomainUnitOfWork, NSBUnitOfWork>());

                container.AddTransient<IEventFactory, EventFactory>();
                container.AddTransient<Contracts.IMessageDispatcher, Dispatcher>();
                container.AddTransient<IMessaging, NServiceBusMessaging>();

                container.AddTransient<IMessageSession>((_) => Bus.Instance);


                var nsbSettings = endpointConfig.GetSettings();

                nsbSettings.Set("SlowAlertThreshold", config.SlowAlertThreshold);
                nsbSettings.Set("CommandDestination", config.CommandDestination);


                endpointConfig.MakeInstanceUniquelyAddressable(settings.UniqueAddress);
                // Callbacks need 1 slot so minimum is 2
                //endpointConfig.LimitMessageProcessingConcurrencyTo(2);
                // NSB doesn't have an endpoint name setter other than the constructor, hack it in
                nsbSettings.Set("NServiceBus.Routing.EndpointName", settings.Endpoint);

                var recoverability = endpointConfig.Recoverability();
                recoverability.Failed(recovery =>
                {
                    recovery.OnMessageSentToErrorQueue((message, token) =>
                    {
                        var loggerFactory = settings.Configuration.ServiceProvider.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("Recoverability");

                        var ex = message.Exception;
                        var messageText = Encoding.UTF8.GetString(message.Body.Span).MaxLines(10);
                        logger.ErrorEvent("Fault", ex, "[{MessageId:l}] has failed and being sent to error queue [{ErrorQueue}]: {ExceptionType} - {ExceptionMessage}\n{@Body}",
                            message.MessageId, message.ErrorQueue, ex.GetType().Name, ex.Message, messageText);
                        return Task.CompletedTask;
                    });
                });

                // business exceptions are permentant and shouldnt be retried
                recoverability.AddUnrecoverableException<BusinessException>();
                recoverability.AddUnrecoverableException<SagaWasAborted>();
                recoverability.AddUnrecoverableException<SagaAbortionFailureException>();

                // we dont need immediate retries
                recoverability.Immediate(recovery => recovery.NumberOfRetries(0));

                recoverability.Delayed(recovery =>
                {
                    recovery.TimeIncrease(TimeSpan.FromSeconds(2));
                    recovery.NumberOfRetries(config.Retries);
                    recovery.OnMessageBeingRetried((message, token) =>
                    {
                        var loggerFactory = settings.Configuration.ServiceProvider.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("Recoverability");

                        var level = LogLevel.Information;
                        if (message.RetryAttempt > (config.Retries / 2))
                            level = LogLevel.Warning;

                        var ex = message.Exception;

                        var messageText = Encoding.UTF8.GetString(message.Body.Span).MaxLines(10);
                        logger.LogEvent(level, "Catch", ex, "[{MessageId:l}] has failed and will retry {Attempts} more times: {ExceptionType} - {ExceptionMessage}\n{@Body}", message.MessageId,
                            config.Retries - message.RetryAttempt, ex?.GetType().Name, ex?.Message, messageText);
                        return Task.CompletedTask;
                    });
                });

                // todo: not sure this is needed anymore since NSb uses microsoft too now
                startableEndpoint = EndpointWithExternallyManagedContainer.Create(endpointConfig, container);

                return Task.CompletedTask;
            });

            Settings.StartupTasks.Add((container, settings) =>
            {
                var logFactory = container.GetService<ILoggerFactory>();
                if (logFactory != null)
                    global::NServiceBus.Logging.LogManager.UseFactory(new ExtensionsLoggerFactory(logFactory));

                return Aggregates.Bus.Start(container, startableEndpoint);
            });
            Settings.ShutdownTasks.Add((container, settings) =>
            {
                return Aggregates.Bus.Instance.Stop();
            });

            return config;
        }

    }
}
