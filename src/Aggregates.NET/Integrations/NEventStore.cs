using Aggregates.Integrations;
using NEventStore;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class NEventStore : Wireup
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(NEventStore));

        private readonly BusConfiguration _config;
        public NEventStore(Wireup wireup, BusConfiguration config) : base(wireup)
        {
            _config = config;

            // Configures NEventstore to now build until IStoreEvents is actually requested, because at this point in the pipeline the bus does not exist yet
            config.RegisterComponents(x => x.ConfigureComponent<IStoreEvents>(y =>
            {
                Logger.Debug("Configuring the store to dispatch messages synchronously.");

                // Dispatcher is a proxy class which uses IEventPublisher to get the current UnitOfWork for publishing events to the bus
                wireup.UsingSynchronousDispatchScheduler(
                    new Dispatcher(y.Build<IBus>())
                    );

                return wireup.Build();

            }, DependencyLifecycle.SingleInstance));
        }

        public override IStoreEvents Build()
        {
            throw new NotImplementedException("Do not call NEventstore.Build yourself, after .UseAggregates you are done");
        }
    }

    public static class WireupExtension
    {
        public static NEventStore UseAggregates(this Wireup wireup, BusConfiguration config)
        {
            return new NEventStore(wireup, config);
        }
    }
}
