using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    class Feature : NServiceBus.Features.Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            var settings = context.Settings;
            var aggSettings = settings.Get<ISettings>(NSBDefaults.AggregatesSettings);

            context.Pipeline.Register<ScopedProviderRegistration>();
            context.Pipeline.Register<FailureReplyRegistration>();

            context.Pipeline.Register<MutateIncomingRegistration>();
            context.Pipeline.Register<MutateOutgoingRegistration>();

            context.Pipeline.Register<UowRegistration>();
            context.Pipeline.Register<CommandAcceptorRegistration>();
            context.Pipeline.Register<SagaBehaviourRegistration>();

            // Remove NSBs unit of work since we do it ourselves
            context.Pipeline.Remove("ExecuteUnitOfWork");


            context.Pipeline.Register<LogContextProviderRegistration>();

            if (aggSettings.SlowAlertThreshold.HasValue)
                context.Pipeline.Register<TimeExecutionRegistration>();
            var types = settings.GetAvailableTypes();

            context.Pipeline.Register<MessageIdentifierRegistration>();
            context.Pipeline.Register<MessageDetyperRegistration>();

            // Register all service handlers in my IoC so query processor can use them
            foreach (var type in types.Where(IsServiceHandler))
                context.Container.ConfigureComponent(type, DependencyLifecycle.InstancePerCall);


            // We are sending IEvents, which NSB doesn't like out of the box - so turn that check off
            context.Pipeline.Remove("EnforceSendBestPractices");

            context.RegisterStartupTask(builder => new EndpointRunner(builder.Build<ILogger<EndpointRunner>>(), context.Settings.InstanceSpecificQueue(), aggSettings));
        }
        private static bool IsServiceHandler(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
                return false;

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IProvideService<,>));
        }
    }

    [ExcludeFromCodeCoverage]
    class EndpointRunner : FeatureStartupTask
    {
        private readonly ILogger Logger;
        private readonly string _instanceQueue;
        private readonly ISettings _settings;

        public EndpointRunner(ILogger<EndpointRunner> logger, string instanceQueue, ISettings settings)
        {
            Logger = logger;
            _instanceQueue = instanceQueue;
            _settings = settings;
        }
        protected override async Task OnStart(IMessageSession session)
        {

            Logger.InfoEvent("Startup", "Starting on {Queue}", _instanceQueue);

            await session.Publish<EndpointAlive>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

        }
        protected override async Task OnStop(IMessageSession session)
        {

            Logger.InfoEvent("Shutdown", "Stopping on {Queue}", _instanceQueue);
            await session.Publish<EndpointDead>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

        }
    }
}
