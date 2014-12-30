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
    public class Dispatcher : IDispatchCommits
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Dispatcher));

        private readonly IBus _bus;
        public Dispatcher(IBus bus)
        {
            _bus = bus;
        }

        public virtual void Dispatch(ICommit commit)
        {
            // Possibly add commit.headers to bus, but would only be used for headers Aggregates.NET adds which atm is useless
            foreach (var @event in commit.Events)
                _bus.Publish(@event.Body);

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
