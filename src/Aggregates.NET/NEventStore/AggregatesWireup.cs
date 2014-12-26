using System.Collections.Generic;
using System.Collections.ObjectModel;
using Aggregates.Contracts;
using NEventStore;
using NEventStore.Dispatcher;
using NEventStore.Logging;
using NEventStore.Persistence;
using NEventStore.Serialization;
using NServiceBus.ObjectBuilder;

namespace Aggregates.NEventStore
{
    public class AggregatesWireup : Wireup
    {
        private static readonly ILog Logger = LogFactory.BuildLogger(typeof(AggregatesWireup));

        public AggregatesWireup(Wireup wireup, IBuilder builder )
            : base(wireup)
        {
            Logger.Debug("Configuring the store to dispatch messages synchronously.");
            
            wireup.UsingAsynchronousDispatchScheduler();
            Container.Register<IDispatchCommits>((c) => builder.Build<IDispatchCommits>());
        }


        public override IStoreEvents Build()
        {
            Logger.Debug("Configuring the store to upconvert events when fetched.");

            var pipelineHooks = Container.Resolve<ICollection<IPipelineHook>>();

            if (pipelineHooks == null)
            {
                Container.Register((pipelineHooks = new Collection<IPipelineHook>()));
            }
                        
            var store = base.Build();
            
            return store;
        }
    }
    
    public static AggregatesWireup Aggregates(this Wireup wireup, IBuilder builder)
    {
        return new AggregatesWireup(wireup, builder);
    }
}