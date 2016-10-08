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
                s.SetDefault("WatchConflicts", false);
                s.SetDefault("ClaimThreshold", 5);
                s.SetDefault("ExpireConflict", TimeSpan.FromMinutes(1));
                s.SetDefault("ClaimLength", TimeSpan.FromMinutes(10));
                s.SetDefault("CommonalityRequired", 0.9M);
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

            context.Container.ConfigureComponent<Func<IAccept>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return () => eventFactory.CreateInstance<IAccept>();
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent<Func<string, IReject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return message => { return eventFactory.CreateInstance<IReject>(e => { e.Message = message; }); };
            }, DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Func<BusinessException, IReject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return exception => {
                    return eventFactory.CreateInstance<IReject>(e => {
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
                behavior: new CommandUnitOfWork(settings.Get<int>("SlowAlertThreshold")),
                description: "Begins and Ends command unit of work"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateIncomingCommands),
                description: "Running command mutators for incoming messages"
                );

            if(settings.Get<bool>("WatchConflicts"))
                context.Pipeline.Register(
                    behavior: new BulkCommandBehavior(settings.Get<int>("ClaimThreshold"), settings.Get<TimeSpan>("ExpireConflict"),settings.Get<TimeSpan>("ClaimLength"), settings.Get<decimal>("CommonalityRequired"), settings.EndpointName(), settings.InstanceSpecificQueue()),
                    description: "Watches commands for many version conflict exceptions, when found it will claim the command type with a byte mask to run only on 1 instance reducing eventstore conflicts considerably"
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
                await session.Publish<IDomainAlive>(x =>
                {
                    x.Endpoint = _settings.InstanceSpecificQueue();
                    x.Instance = Aggregates.Defaults.Instance;
                }).ConfigureAwait(false);

            }
            protected override async Task OnStop(IMessageSession session)
            {
                Logger.Write(LogLevel.Debug, "Stopping domain");
                await session.Publish<IDomainDead>(x =>
                {
                    x.Endpoint = _settings.InstanceSpecificQueue();
                    x.Instance = Aggregates.Defaults.Instance;
                }).ConfigureAwait(false);
            }
        }
    }
}