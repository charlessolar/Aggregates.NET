using System;
using System.Text;
using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Features;

namespace Aggregates
{
    public class Feature : NServiceBus.Features.Feature
    {
        public Feature()
        {
            Defaults(s =>
            {
                s.SetDefault("ImmediateRetries", 3);
                s.SetDefault("RetryForever", false);
                s.SetDefault("DelayedRetries", 3);
                s.SetDefault("SlowAlertThreshold", 1000);
                s.SetDefault("ReadSize", 200);
                s.SetDefault("Compress", false);
            });
            
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            // Check that aggregates has been properly setup
            if (!context.Settings.Get<bool>(Aggregates.Defaults.SetupCorrectly))
                throw new InvalidOperationException("Endpoint not setup correctly!  Please call [endpointConfiguration.Recoverability.ConfigureForAggregates] before enabling this feature.  (Sorry I can't set recoverability myself)");
            
            var settings = context.Settings;
            context.Pipeline.Register(
                behavior: new ExceptionRejector(settings.Get<int>("ImmediateRetries"), settings.Get<bool>("RetryForever")),
                description: "Watches message faults, sends error replies to client when message moves to error queue"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateOutgoingCommands),
                description: "runs command mutators on outgoing commands"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateOutgoingEvents),
                description: "runs command mutators on outgoing events"
                );

            // We are sending IEvents, which NSB doesn't like out of the box - so turn that check off
            context.Pipeline.Remove("EnforceSendBestPractices");

            context.Container.ConfigureComponent<Func<Exception, string, IError>>(y =>
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
                        return eventFactory.CreateInstance<IError>(e => { e.Message = sb.ToString(); });

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

                    return eventFactory.CreateInstance<IError>(e => {
                        e.Message = sb.ToString();
                    });
                };
            }, DependencyLifecycle.SingleInstance);
        }
    }
}
