using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Logging;
using Aggregates.Messages;
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
        public Feature()
        {
            DependsOn("NServiceBus.Features.ReceiveFeature");
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            var settings = context.Settings;
            var container = Configuration.Settings.Container;

            context.Pipeline.Register<ExceptionRejectorRegistration>();

            if (!Configuration.Settings.Passive)
            {
                MutationManager.RegisterMutator("domain unit of work", typeof(UnitOfWork.IDomain));


                context.Pipeline.Register<UowRegistration>();
                context.Pipeline.Register<CommandAcceptorRegistration>();
                context.Pipeline.Register<SagaBehaviourRegistration>();

                // Remove NSBs unit of work since we do it ourselves
                context.Pipeline.Remove("ExecuteUnitOfWork");

                // bulk invoke only possible with consumer feature because it uses the eventstore as a sink when overloaded
                context.Pipeline.Replace("InvokeHandlers", (b) =>
                    new BulkInvokeHandlerTerminator(b.Build<IMetrics>(), b.Build<IEventMapper>()),
                    "Replaces default invoke handlers with one that supports our custom delayed invoker");
            }

            context.Pipeline.Register<LocalMessageUnpackRegistration>();
            context.Pipeline.Register<LogContextProviderRegistration>();

            if (Configuration.Settings.SlowAlertThreshold.HasValue)
                context.Pipeline.Register(
                    behavior: new TimeExecutionBehavior(),
                    description: "times the execution of messages and reports anytime they are slow"
                    );

            var types = settings.GetAvailableTypes();

            var messageMetadataRegistry = settings.Get<MessageMetadataRegistry>();
            context.Pipeline.Register<MessageIdentifierRegistration>();
            context.Pipeline.Register<MessageDetyperRegistration>();

            // Register all service handlers in my IoC so query processor can use them
            foreach (var type in types.Where(IsServiceHandler))
                container.Register(type, Lifestyle.PerInstance);

            context.Pipeline.Register<MutateIncomingRegistration>();
            context.Pipeline.Register<MutateOutgoingRegistration>();
            
            // We are sending IEvents, which NSB doesn't like out of the box - so turn that check off
            context.Pipeline.Remove("EnforceSendBestPractices");

            context.RegisterStartupTask(builder => new EndpointRunner(context.Settings.InstanceSpecificQueue(), Configuration.Settings, Configuration.Settings.StartupTasks, Configuration.Settings.ShutdownTasks));
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
        private static readonly ILog Logger = LogProvider.GetLogger("EndpointRunner");
        private readonly String _instanceQueue;
        private readonly Configure _config;
        private readonly IEnumerable<Func<Configure, Task>> _startupTasks;
        private readonly IEnumerable<Func<Configure, Task>> _shutdownTasks;

        public EndpointRunner(String instanceQueue, Configure config, IEnumerable<Func<Configure, Task>> startupTasks, IEnumerable<Func<Configure, Task>> shutdownTasks)
        {
            _instanceQueue = instanceQueue;
            _config = config;
            _startupTasks = startupTasks;
            _shutdownTasks = shutdownTasks;
        }
        protected override async Task OnStart(IMessageSession session)
        {
            // Subscribe to BulkMessage, because it wraps messages and is not used in a handler directly
            if(!_config.Passive)
                await session.Subscribe<BulkMessage>().ConfigureAwait(false);

            Logger.InfoEvent("Startup", "Starting on {Queue}", _instanceQueue);

            await session.Publish<EndpointAlive>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

            // Don't stop the bus from completing setup
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _startupTasks.WhenAllAsync(x => x(_config)).Wait();
            });
        }
        protected override async Task OnStop(IMessageSession session)
        {
            Logger.InfoEvent("Shutdown", "Stopping on {Queue}", _instanceQueue);
            await session.Publish<EndpointDead>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

            await _shutdownTasks.WhenAllAsync(x => x(_config)).ConfigureAwait(false);
        }
    }
}
