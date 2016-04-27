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
            context.Pipeline.Register<SafetyNetRegistration>();
            context.Pipeline.Register<ExceptionRejectorRegistration>();

            context.Container.ConfigureComponent<Func<Exception, Error>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (exception) => {
                    return eventFactory.CreateInstance<Error>(e => {
                        e.Exception = exception;
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
