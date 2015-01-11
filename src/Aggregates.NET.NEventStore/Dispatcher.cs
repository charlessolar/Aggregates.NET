using Aggregates.Contracts;
using NEventStore;
using NEventStore.Dispatcher;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Integrations
{
    public class Dispatcher : IDispatchCommits, IEventPublisher
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Dispatcher));

        private readonly IBus _bus;

        public Dispatcher(IBus bus)
        {
            _bus = bus;
        }

        public void Dispatch(ICommit commit)
        {
            this.Publish(commit.Events.Select(e => e.Body));
        }

        public void Publish(IEnumerable<Object> events)
        {
            foreach (var @event in events)
                _bus.Publish(@event);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}