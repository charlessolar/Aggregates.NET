using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates
{
    public class Feature : NServiceBus.Features.Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<ExceptionFilter>(DependencyLifecycle.InstancePerCall);
            
            context.Container.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<Processor>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<Func<Accept>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return () => { return eventFactory.CreateInstance<Accept>(); };
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent<Func<String, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (message) => { return eventFactory.CreateInstance<Reject>(e => { e.Message = message; }); };
            }, DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Func<Exception, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (exception) => {
                    return eventFactory.CreateInstance<Reject>(e => {
                        e.Message = "Exception raised";
                        e.Exception = exception;
                    });
                };
            }, DependencyLifecycle.SingleInstance);

            context.Pipeline.Register<ExceptionFilterRegistration>();
            context.Pipeline.Register<BuilderInjectorRegistration>();
            context.Pipeline.Register<SafetyNetRegistration>();

            // Register all query handlers in the container
            foreach (var handler in context.Settings.GetAvailableTypes().Where(IsQueryOrComputeHandler))
                context.Container.ConfigureComponent(handler, DependencyLifecycle.InstancePerUnitOfWork);
            
        }
        public static bool IsQueryOrComputeHandler(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
            {
                return false;
            }

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleQueries<,>) || genericTypeDef == typeof(IHandleComputed<,>));
        }
    }
}