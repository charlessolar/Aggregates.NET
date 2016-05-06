using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class Feature : NServiceBus.Features.Feature
    {
        public Feature()
        {
            Defaults(s =>
            {
                s.SetDefault("MaxRetries", 10);
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<DefaultInvokeObjects>(DependencyLifecycle.SingleInstance);


            context.Pipeline.Replace(WellKnownStep.LoadHandlers, typeof(AsyncronizedLoad), "Loads the message handlers");
            context.Pipeline.Replace(WellKnownStep.InvokeHandlers, typeof(AsyncronizedInvoke), "Invokes the message handler with Task.Run");
            context.Pipeline.Register<ExceptionRejectorRegistration>();

            context.Container.ConfigureComponent<Func<Exception, Error>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (exception) => {
                    var message =
                        "Exception type " + exception.GetType() + Environment.NewLine +
                        "Exception message: " + exception.Message + Environment.NewLine +
                        "Stack trace: " + exception.StackTrace + Environment.NewLine;

                    if(exception.InnerException != null)
                    {
                        message += "---BEGIN InnerException--- " + Environment.NewLine +
                           "Exception type " + exception.InnerException.GetType() + Environment.NewLine +
                           "Exception message: " + exception.InnerException.Message + Environment.NewLine +
                           "Stack trace: " + exception.InnerException.StackTrace + Environment.NewLine +
                           "---END Inner Exception";
                    }
                    if(exception is System.AggregateException)
                    {
                        message += "---BEGIN Aggregate Exception---";
                        var aggException = exception as System.AggregateException;
                        foreach( var inner in aggException.InnerExceptions)
                        {
                            message += "---BEGIN InnerException--- " + Environment.NewLine +
                               "Exception type " + inner.GetType() + Environment.NewLine +
                               "Exception message: " + inner.Message + Environment.NewLine +
                               "Stack trace: " + inner.StackTrace + Environment.NewLine +
                               "---END Inner Exception";
                        }
                    }

                    return eventFactory.CreateInstance<Error>(e => {
                        e.Message = message;
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
