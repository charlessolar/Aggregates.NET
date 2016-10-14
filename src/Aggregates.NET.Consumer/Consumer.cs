using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Features;

namespace Aggregates
{
    public class ConsumerFeature : Feature
    {
        public ConsumerFeature()
        {
            Defaults(s =>
            {
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);
            
            context.Pipeline.Register(
                behavior: typeof(MutateIncomingEvents),
                description: "Running event mutators for incoming messages"
                );
            context.Pipeline.Register(
                behavior: typeof(EventUnitOfWork),
                description: "Begins and Ends event unit of work"
                );
        }
    }

    public class DurableConsumer : ConsumerFeature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);

            context.Container.ConfigureComponent<Checkpointer>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DurableSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }

    public class VolatileConsumer : ConsumerFeature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);

            context.Container.ConfigureComponent<VolatileSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }
    public class CompetingConsumer : ConsumerFeature
    {
        public CompetingConsumer()
        {
            Defaults(s =>
            {
                s.SetDefault("BucketHeartbeats", 5);
                s.SetDefault("BucketExpiration", 20);
                s.SetDefault("BucketCount", 1);
                s.SetDefault("BucketsHandled", 1);
                s.SetDefault("PauseOnFreeBuckets", true);
            });
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);

            context.Container.ConfigureComponent<CompetingSubscriber>(DependencyLifecycle.SingleInstance);
        }
    }
    
}