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
    public class Checkpointer : IEventUnitOfWork, IEventMutator
    {
        public Object CurrentMessage { get; private set; }
        public IReadOnlyDictionary<String, String> CurrentHeaders { get; private set; }
        public long? CurrentPosition { get; private set; }

        public IBuilder Builder { get; set; }
        public Int32 Retries { get; set; }

        private readonly IPersistCheckpoints _checkpoints;
        private readonly ReadOnlySettings _settings;
        public Checkpointer(IPersistCheckpoints checkpoints, ReadOnlySettings settings)
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

        public IEvent MutateIncoming(IEvent Event, IReadOnlyDictionary<String, String> Headers)
        {
            this.CurrentHeaders = Headers;
            this.CurrentMessage = Event;
            if (Headers.ContainsKey("CommitPosition"))
                this.CurrentPosition = long.Parse(Headers["CommitPosition"]);
            
            return Event;
        }

        public IEvent MutateOutgoing(IEvent Event)
        {
            return Event;
        }
    }
}
