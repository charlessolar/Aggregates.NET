using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;
using Aggregates.Contracts;
using NServiceBus.Settings;
using NServiceBus;

namespace Aggregates.Internal
{
    public class EventUnitOfWork : IEventUnitOfWork, IEventMutator
    {
        public Object CurrentEvent { get; private set; }
        public IEventDescriptor CurrentDescriptor { get; private set; }
        public long? CurrentPosition { get; private set; }

        public IBuilder Builder { get; set; }
        
        private readonly ReadOnlySettings _settings;
        public EventUnitOfWork(ReadOnlySettings settings)
        {
            _settings = settings;
        }

        public void Begin()
        {
        }

        public void End(Exception ex = null)
        {
            if (ex != null) return;
            if (this.CurrentPosition.HasValue)
            {
                // IPersistCheckpoints can't be in the constructor because it will depend on a IEventUnitOfWork
                // Which would cause the builder to delivery 2 diffent uow to us and the app
                var checkpoints = Builder.Build<IPersistCheckpoints>();
                checkpoints.Save(_settings.EndpointName(), CurrentPosition.Value);
            }
        }

        public object MutateIncoming(object Event, IEventDescriptor Descriptor, long? Position)
        {
            this.CurrentDescriptor = Descriptor;
            this.CurrentEvent = Event;
            this.CurrentPosition = Position;
            return Event;
        }

        public IWritableEvent MutateOutgoing(IWritableEvent Event)
        {
            return Event;
        }
    }
}
