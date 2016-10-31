using System;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.Settings;

namespace Aggregates
{
    public class Domain : ConsumerFeature
    {
        public Domain()
        {
            Defaults(s =>
            {
                s.SetDefault("ShouldCacheEntities", false);
                s.SetDefault("MaxConflictResolves", 3);
                s.SetDefault("StreamGenerator", new StreamIdGenerator((type, bucket, stream) => $"{bucket}.[{type.FullName}].{stream}"));
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);

            context.RegisterStartupTask(() => new DomainStart(context.Settings));

            context.Container.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<Processor>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<MemoryStreamCache>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<ResolveStronglyConflictResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<ResolveWeaklyConflictResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DiscardConflictResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<IgnoreConflictResolver>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<Func<Accept>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return () => eventFactory.CreateInstance<Accept>();
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent<Func<string, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return message => { return eventFactory.CreateInstance<Reject>(e => { e.Message = message; }); };
            }, DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Func<BusinessException, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return exception => {
                    return eventFactory.CreateInstance<Reject>(e => {
                        e.Message = "Exception raised";
                    });
                };
            }, DependencyLifecycle.SingleInstance);


            var settings = context.Settings;
            context.Pipeline.Register(
                behavior: typeof(CommandAcceptor),
                description: "Filters [BusinessException] from processing failures"
                );
            context.Pipeline.Register(
                behavior: typeof(CommandUnitOfWork),
                description: "Begins and Ends command unit of work"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateIncomingCommands),
                description: "Running command mutators for incoming messages"
                );
            

            //context.Pipeline.Register<SafetyNetRegistration>();
            //context.Pipeline.Register<TesterBehaviorRegistration>();
        }

        public class DomainStart : FeatureStartupTask
        {
            private static readonly ILog Logger = LogManager.GetLogger(typeof(DomainStart));

            private readonly ReadOnlySettings _settings;

            public DomainStart(ReadOnlySettings settings)
            {
                _settings = settings;
            }

            protected override async Task OnStart(IMessageSession session)
            {
                Logger.Write(LogLevel.Debug, "Starting domain");
                await session.Publish<DomainAlive>(x =>
                {
                    x.Endpoint = _settings.InstanceSpecificQueue();
                    x.Instance = Aggregates.Defaults.Instance;
                }).ConfigureAwait(false);

            }
            protected override async Task OnStop(IMessageSession session)
            {
                Logger.Write(LogLevel.Debug, "Stopping domain");
                await session.Publish<DomainDead>(x =>
                {
                    x.Endpoint = _settings.InstanceSpecificQueue();
                    x.Instance = Aggregates.Defaults.Instance;
                }).ConfigureAwait(false);
            }
        }
    }
}