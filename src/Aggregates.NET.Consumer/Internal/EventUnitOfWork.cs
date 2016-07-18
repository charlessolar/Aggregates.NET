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
        public Int32 Retries { get; set; }

        private readonly IPersistCheckpoints _checkpoints;
        private readonly ReadOnlySettings _settings;
        public EventUnitOfWork(IPersistCheckpoints checkpoints, ReadOnlySettings settings)
        {
            _checkpoints = checkpoints;
            _settings = settings;
        }

        public Task Begin()
        {
            return Task.FromResult(true);
        }

        public async Task End(Exception ex = null)
        {
            if (ex != null) return;
            if(this.CurrentPosition.HasValue)
                await _checkpoints.Save(_settings.EndpointName(), CurrentPosition.Value);
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
