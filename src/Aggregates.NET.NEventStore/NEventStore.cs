using Aggregates.Integrations;
using NEventStore;
using NEventStore.Dispatcher;
using NEventStore.Persistence;
using NEventStore.Serialization;
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
    public class NEventStore : Wireup
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(NEventStore));

        public IBuilder Builder { get; private set; }

        public NEventStore(Wireup inner) : base(inner) { }

        public NEventStore(Wireup wireup, IBuilder builder)
            : base(wireup)
        {
            Builder = builder;
        }

        public override IStoreEvents Build()
        {
            Container.Register<IDispatchCommits>(c => Builder.Build<IDispatchCommits>());

            Container.Register<IScheduleDispatches>(c =>
            {
                var dispatchScheduler = new SynchronousDispatchScheduler(
                    c.Resolve<IDispatchCommits>(),
                    c.Resolve<IPersistStreams>());

                dispatchScheduler.Start();
                return dispatchScheduler;
            });
            var store = base.Build();
            Container.Register(store);

            return store;
        }
    }


    public static class WireupExtension
    {
        public static NEventStore UseAggregates(this Wireup wireup, IBuilder builder)
        {
            return new NEventStore(wireup, builder);
        }
    }
}
