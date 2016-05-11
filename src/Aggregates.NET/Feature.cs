using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Config;
using NServiceBus.Config.ConfigurationSource;
using NServiceBus.Features;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class Feature : NServiceBus.Features.Feature, IProvideConfiguration<TransportConfig>
    {
        public Feature()
        {
            Defaults(s =>
            {
                s.SetDefault("MaxRetries", 10);
                s.SetDefault("SlowAlertThreshold", 500);
            });
        }
        public TransportConfig GetConfiguration()
        {
            // Set a large amount of retries, when MaxRetries is hit ExceptionRejector will stop processing the message
            return new TransportConfig
            {
                MaxRetries = 999
            };
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<DefaultInvokeObjects>(DependencyLifecycle.SingleInstance);


            context.Pipeline.Replace(WellKnownStep.LoadHandlers, typeof(AsyncronizedLoad), "Loads the message handlers");
            context.Pipeline.Replace(WellKnownStep.InvokeHandlers, typeof(AsyncronizedInvoke), "Invokes the message handler with Task.Run");
            context.Pipeline.Register<ExceptionRejectorRegistration>();

            context.Container.ConfigureComponent<Func<Exception, String, Error>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (exception, message) => {
                    var sb = new StringBuilder();
                    if (!String.IsNullOrEmpty(message))
                    {
                        sb.AppendLine($"Error Message: {message}");
                    }
                    sb.AppendLine($"Exception type {exception.GetType()}");
                    sb.AppendLine($"Exception message: {exception.Message}");
                    sb.AppendLine($"Stack trace: {exception.StackTrace}");
                    

                    if(exception.InnerException != null)
                    {
                        sb.AppendLine("---BEGIN Inner Exception--- ");
                        sb.AppendLine($"Exception type {exception.InnerException.GetType()}");
                        sb.AppendLine($"Exception message: {exception.InnerException.Message}");
                        sb.AppendLine($"Stack trace: {exception.InnerException.StackTrace}");
                        sb.AppendLine("---END Inner Exception---");
                        
                    }
                    if(exception is System.AggregateException)
                    {
                        sb.AppendLine("---BEGIN Aggregate Exception---");
                        var aggException = exception as System.AggregateException;
                        foreach( var inner in aggException.InnerExceptions)
                        {

                            sb.AppendLine("---BEGIN Inner Exception--- ");
                            sb.AppendLine($"Exception type {inner.GetType()}");
                            sb.AppendLine($"Exception message: {inner.Message}");
                            sb.AppendLine($"Stack trace: {inner.StackTrace}");
                            sb.AppendLine("---END Inner Exception---");
                        }
                    }

                    return eventFactory.CreateInstance<Error>(e => {
                        e.Message = sb.ToString();
                    });
                };
            }, DependencyLifecycle.SingleInstance);

            foreach (var handler in context.Settings.GetAvailableTypes().Where(IsAsyncMessage))
                context.Container.ConfigureComponent(handler, DependencyLifecycle.InstancePerCall);
        }
        private static bool IsAsyncMessage(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
            {
                return false;
            }

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleMessagesAsync<>));
        }
    }
}
