using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Logging;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.MessageInterfaces;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
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

            container.Register<IDomainUnitOfWork, NSBUnitOfWork>();
            MutationManager.RegisterMutator("domain unit of work", typeof(IDomainUnitOfWork));

            //context.Container.ConfigureComponent<NSBUnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<IEventFactory>((c) => new EventFactory(c.Build<IMessageCreator>()), DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<IMessageDispatcher>((c) => new Dispatcher(c.Build<IMetrics>(), c.Build<IMessageSerializer>(), c.Build<IMessageSession>(), c.Build<IEventMapper>()), DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<IMessaging>((c) => new NServiceBusMessaging(c.Build<MessageHandlerRegistry>(), c.Build<MessageMetadataRegistry>()), DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<IEventMapper>((c) => new EventMapper(c.Build<IMessageMapper>()), DependencyLifecycle.InstancePerCall);


            context.Pipeline.Register(
                b => new ExceptionRejector(b.Build<IMetrics>(), Configuration.Settings.Retries),
                "Watches message faults, sends error replies to client when message moves to error queue"
                );

            if (Configuration.Settings.SlowAlertThreshold.HasValue)
                context.Pipeline.Register(
                    behavior: new TimeExecutionBehavior(Configuration.Settings.SlowAlertThreshold.Value),
                    description: "times the execution of messages and reports anytime they are slow"
                    );

            var types = settings.GetAvailableTypes();

            // Register all query handlers in my IoC so query processor can use them
            foreach (var type in types.Where(IsQueryHandler))
                container.Register(type);

            context.Pipeline.Register<CommandAcceptorRegistration>();
            context.Pipeline.Register<UowRegistration>();
            context.Pipeline.Register<MutateIncomingRegistration>();
            context.Pipeline.Register<MutateOutgoingRegistration>();

            // We are sending IEvents, which NSB doesn't like out of the box - so turn that check off
            context.Pipeline.Remove("EnforceSendBestPractices");

            // bulk invoke only possible with consumer feature because it uses the eventstore as a sink when overloaded
            context.Pipeline.Replace("InvokeHandlers", (b) =>
                new BulkInvokeHandlerTerminator(container.Resolve<IMetrics>(), b.Build<IEventMapper>()),
                "Replaces default invoke handlers with one that supports our custom delayed invoker");


            context.RegisterStartupTask(builder => new EndpointRunner(context.Settings.InstanceSpecificQueue(), Configuration.Settings, Configuration.Settings.StartupTasks, Configuration.Settings.ShutdownTasks));
        }
        private static bool IsQueryHandler(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
                return false;

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleQueries<,>));
        }
    }

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
            // Weird place for a registration but NSB doesn't make getting IMessageSession easy.
            // its never registered in their container so we have to register it ourselves
            // and since this method is run before Bus.Start is done we can't register IMessageSession later
            Configuration.Settings.Container.RegisterSingleton(session);

            Logger.Write(LogLevel.Info, "Starting endpoint");

            await session.Publish<EndpointAlive>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

            foreach (var task in _startupTasks)
                await task(_config);
        }
        protected override async Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Stopping endpoint");
            await session.Publish<EndpointDead>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

            foreach (var task in _shutdownTasks)
                await task(_config);
        }
    }
}
