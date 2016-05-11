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
using NServiceBus.Config.ConfigurationSource;
using NServiceBus.Config;

namespace Aggregates
{
    public class Domain : Aggregates.Feature, IProvideConfiguration<TransportConfig>
    {
        public Domain() : base()
        {
            Defaults(s =>
            {
                s.SetDefault("ShouldCacheEntities", false);
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


                    if (exception.InnerException != null)
                    {
                        sb.AppendLine("---BEGIN Inner Exception--- ");
                        sb.AppendLine($"Exception type {exception.InnerException.GetType()}");
                        sb.AppendLine($"Exception message: {exception.InnerException.Message}");
                        sb.AppendLine($"Stack trace: {exception.InnerException.StackTrace}");
                        sb.AppendLine("---END Inner Exception---");

                    }
                    if (exception is System.AggregateException)
                    {
                        sb.AppendLine("---BEGIN Aggregate Exception---");
                        var aggException = exception as System.AggregateException;
                        foreach (var inner in aggException.InnerExceptions)
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

            context.Pipeline.Register<CommandAcceptorRegistration>();
            context.Pipeline.Register<CommandUnitOfWorkRegistration>();
            context.Pipeline.Register<ExceptionRejectorRegistration>();
            //context.Pipeline.Register<SafetyNetRegistration>();
            //context.Pipeline.Register<TesterBehaviorRegistration>();

            // Register all query and computed in the container
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
                .Any(genericTypeDef => genericTypeDef == typeof(IHandleQueries<,>) || genericTypeDef == typeof(IHandleComputed<,>));
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