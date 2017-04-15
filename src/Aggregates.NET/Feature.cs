using System;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates
{
    public class Feature : NServiceBus.Features.Feature
    {
        public Feature()
        {
            DependsOn("NServiceBus.Features.ReceiveFeature");
            Defaults(s =>
            {
                s.SetDefault("Retries", 10);
                s.SetDefault("ReadSize", 200);
                s.SetDefault("Compress", Compression.Snapshots);
                s.SetDefault("SlowAlertThreshold", 1000);
                s.SetDefault("SlowAlerts", false);
                s.SetDefault("MaxDelayed", 1000);
                s.SetDefault("StreamGenerator", new StreamIdGenerator((type, streamType, bucket, stream, parents) => $"{streamType}-{bucket}-{parents.BuildParentsString()}-{type.FullName.Replace(".", "")}-{stream}"));
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask(builder => new EndpointRunner(context.Settings.InstanceSpecificQueue()));

            context.Container.ConfigureComponent<IntelligentCache>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<IDelayedChannel>(() => null, DependencyLifecycle.InstancePerCall);

            // Check that aggregates has been properly setup
            if (!context.Settings.Get<bool>(Aggregates.Defaults.SetupCorrectly))
                throw new InvalidOperationException("Endpoint not setup correctly! Please call [endpointConfiguration.Recoverability.ConfigureForAggregates] before enabling this feature.  (Sorry I can't set recoverability myself)");
            
            var settings = context.Settings;
            context.Pipeline.Register(
                b => new ExceptionRejector(settings.Get<int>("Retries")),
                "Watches message faults, sends error replies to client when message moves to error queue"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateOutgoingCommands),
                description: "runs command mutators on outgoing commands"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateOutgoingEvents),
                description: "runs command mutators on outgoing events"
                );


            if(context.Settings.Get<bool>("SlowAlerts"))
                context.Pipeline.Register(
                    behavior: new TimeExecutionBehavior(settings.Get<int>("SlowAlertThreshold")), 
                    description: "times the execution of messages and reports anytime they are slow"
                    );

            context.Pipeline.Register<ApplicationUowRegistration>();
            // We are sending IEvents, which NSB doesn't like out of the box - so turn that check off
            context.Pipeline.Remove("EnforceSendBestPractices");

            context.Container.ConfigureComponent<Func<Exception, string, Error>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (exception, message) => {
                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(message))
                    {
                        sb.AppendLine($"Error Message: {message}");
                    }
                    sb.AppendLine($"Exception type {exception.GetType()}");
                    sb.AppendLine($"Exception message: {exception.Message}");
                    sb.AppendLine($"Stack trace: {exception.StackTrace}");


                    if (exception.InnerException != null)
                    {
                        sb.AppendLine("---BEGIN Inner Exception--- ");
                        sb.AppendLine($"Exception type {exception.InnerException.GetType()}");
                        sb.AppendLine($"Exception message: {exception.InnerException.Message}");
                        sb.AppendLine($"Stack trace: {exception.InnerException.StackTrace}");
                        sb.AppendLine("---END Inner Exception---");

                    }
                    var aggregateException = exception as AggregateException;
                    if (aggregateException == null)
                        return eventFactory.CreateInstance<Error>(e => { e.Message = sb.ToString(); });

                    sb.AppendLine("---BEGIN Aggregate Exception---");
                    var aggException = aggregateException;
                    foreach (var inner in aggException.InnerExceptions)
                    {

                        sb.AppendLine("---BEGIN Inner Exception--- ");
                        sb.AppendLine($"Exception type {inner.GetType()}");
                        sb.AppendLine($"Exception message: {inner.Message}");
                        sb.AppendLine($"Stack trace: {inner.StackTrace}");
                        sb.AppendLine("---END Inner Exception---");
                    }

                    return eventFactory.CreateInstance<Error>(e => {
                        e.Message = sb.ToString();
                    });
                };
            }, DependencyLifecycle.SingleInstance);
        }
    }

    public class EndpointRunner : FeatureStartupTask
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EndpointRunner));
        private readonly String _instanceQueue;

        public EndpointRunner(String instanceQueue)
        {
            _instanceQueue = instanceQueue;
        }
        protected override async Task OnStart(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Starting endpoint");

            await session.Publish<EndpointAlive>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);

        }
        protected override async Task OnStop(IMessageSession session)
        {
            Logger.Write(LogLevel.Info, "Stopping endpoint");
            await session.Publish<EndpointDead>(x =>
            {
                x.Endpoint = _instanceQueue;
                x.Instance = Defaults.Instance;
            }).ConfigureAwait(false);
        }
    }
}
