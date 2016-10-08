using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    public class Checkpointer : IEventUnitOfWork, IEventMutator
    {
        public object CurrentMessage { get; private set; }
        public IReadOnlyDictionary<string, string> CurrentHeaders { get; private set; }
        public long? CurrentPosition { get; private set; }

        public IBuilder Builder { get; set; }
        public int Retries { get; set; }

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
            if(CurrentPosition.HasValue)
                await _checkpoints.Save(_settings.EndpointName(), CurrentPosition.Value).ConfigureAwait(false);
        }

        public IEvent MutateIncoming(IEvent Event, IReadOnlyDictionary<string, string> headers)
        {
            CurrentHeaders = headers;
            CurrentMessage = Event;
            if (headers.ContainsKey("CommitPosition"))
                CurrentPosition = long.Parse(headers["CommitPosition"]);
            
            return Event;
        }

        public IEvent MutateOutgoing(IEvent Event)
        {
            return Event;
        }
    }
}
