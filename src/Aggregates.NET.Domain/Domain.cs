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
using NServiceBus.Pipeline;

namespace Aggregates
{
    public class Domain : NServiceBus.Features.Feature
    {
        public Domain()
        {
            Defaults(s =>
            {
                s.SetDefault("ReadSize", 200);
                s.SetDefault("ShouldCacheEntities", false);
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {            
            context.Container.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<DefaultInvokeObjects>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Processor>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<MemoryStreamCache>(DependencyLifecycle.InstancePerCall);

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

            context.Pipeline.Replace(WellKnownStep.LoadHandlers, typeof(AsyncronizedLoad), "Loads the message handlers");
            context.Pipeline.Replace(WellKnownStep.InvokeHandlers, typeof(AsyncronizedInvoke), "Invokes the message handler with Task.Run");
            context.Pipeline.Register<CommandAcceptorRegistration>();
            context.Pipeline.Register<CommandUnitOfWorkRegistration>();
            //context.Pipeline.Register<SafetyNetRegistration>();
            //context.Pipeline.Register<TesterBehaviorRegistration>();

            // Register all query, computed, and message handlers in the container
            foreach (var handler in context.Settings.GetAvailableTypes().Where(IsQueryOrComputeOrMessageHandler))
                context.Container.ConfigureComponent(handler, DependencyLifecycle.InstancePerCall);
            

        }
        private static bool IsQueryOrComputeOrMessageHandler(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
            {
                return false;
            }

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleQueries<,>) || genericTypeDef == typeof(IHandleComputed<,>) || genericTypeDef == typeof(IHandleMessagesAsync<>));
        }
        private static bool IsSyncMessage(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
            {
                return false;
            }

            return type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType)
                .Select(@interface => @interface.GetGenericTypeDefinition())
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleMessages<>));
        }
    }
}