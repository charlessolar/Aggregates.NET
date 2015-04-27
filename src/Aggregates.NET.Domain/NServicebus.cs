using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class NServicebus
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServicebus));

        /// <summary>
        /// Required: Register Aggregates.NET services into NServicebus
        /// </summary>
        /// <param name="config">NServicebus configuration</param>
        /// <param name="eventStoreBuilder">Function to build the event store</param>
        /// <param name="accept">Optional accept message creator (for accepting commands)</param>
        /// <param name="reject">Optional reject message creator (for rejecting commands)</param>
        public static void UseAggregates(this BusConfiguration config, Func<IAccept> accept = null, Func<String, IReject> reject = null)
        {
            config.RegisterComponents(x =>
            {
                x.ConfigureComponent<ExceptionFilter>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
                x.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
                x.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);


                //x.ConfigureComponent<IStoreEvents>(y => eventStoreBuilder(y), DependencyLifecycle.SingleInstance);

                if (accept == null)
                {
                    x.ConfigureComponent<Func<IAccept>>(y =>
                    {
                        var eventFactory = y.Build<IMessageCreator>();
                        return () => { return eventFactory.CreateInstance<IAccept>(); };
                    }, DependencyLifecycle.InstancePerCall);
                }
                else
                    x.ConfigureComponent<Func<IAccept>>(() => accept, DependencyLifecycle.InstancePerCall);

                if (reject == null)
                {
                    x.ConfigureComponent<Func<String, IReject>>(y =>
                    {
                        var eventFactory = y.Build<IMessageCreator>();
                        return (message) => { return eventFactory.CreateInstance<IReject>(e => { e.Message = message; }); };
                    }, DependencyLifecycle.InstancePerCall);
                }
                else
                    x.ConfigureComponent<Func<String, IReject>>(() => reject, DependencyLifecycle.InstancePerCall);
            });

            config.Pipeline.Register<ExceptionFilterRegistration>();
        }
    }
}