using System;
using System.Linq;
using Aggregates.Contracts;
using Aggregates.Internal;
using System.Collections;
using System.Collections.Generic;
using NServiceBus.Pipeline.Contexts;
using NServiceBus;
using NEventStore;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder.Common;
using NEventStore.Persistence;
using NServiceBus.Unicast.Messages;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{

    public class UnitOfWork : IUnitOfWork
    {
        private const string AggregateTypeHeader = "AggregateType";
        private static readonly ILog Logger = LogManager.GetLogger(typeof(UnitOfWork));
        private readonly IBuilder _builder;
        private readonly IStoreEvents _eventStore;
        private readonly IBus _bus;

        // Header information to take from incoming messages 
        private readonly String[] _carryOverHeaders = {
                                                      "NServiceBus.MessageId",
                                                      "NServiceBus.CorrelationId",
                                                      "NServiceBus.Version",
                                                      "NServiceBus.TimeSent",
                                                      "NServiceBus.ConversationId",
                                                      "CorrId",
                                                      "NServiceBus.OriginatingMachine",
                                                      "NServiceBus.OriginatingEndpoint"
                                                  };

        public IDictionary<String, String> WorkHeaders { get; set; }

        private bool _disposed;
        private IDictionary<Type, IRepository> _repositories;

        public UnitOfWork(IBuilder builder, IStoreEvents eventStore, IBus bus)
        {
            _builder = builder.CreateChildBuilder();
            _eventStore = eventStore;
            _bus = bus;
            _repositories = new Dictionary<Type, IRepository>();
            WorkHeaders = new Dictionary<String, String>();
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            lock (_repositories)
            {
                foreach (var repo in _repositories)
                {
                    repo.Value.Dispose();
                }

                _repositories.Clear();
            }
            _disposed = true;
        }



        public IRepository<T> For<T>() where T : class, IEventSource
        {
            Logger.DebugFormat("Retreiving repository for type {0}", typeof(T));
            var type = typeof(T);

            IRepository repository;
            if (_repositories.TryGetValue(type, out repository))
                return (IRepository<T>)repository;

            var repoType = typeof(Repository<>).MakeGenericType(typeof(T));
            return (IRepository<T>)(_repositories[type] = (IRepository)Activator.CreateInstance(repoType, _builder, _eventStore));
        }

        public void Begin() { }
        public void End(Exception ex)
        {
            if (ex == null)
                Commit();
        }

        public void Commit()
        {
            var commitId = Guid.NewGuid();

            foreach (var repo in _repositories)
            {
                try
                {
                    // Insert all command headers into the commit
                    var headers = new Dictionary<String, String>(WorkHeaders);
                    headers[AggregateTypeHeader] = repo.Key.FullName;

                    repo.Value.Commit(commitId, headers);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            }
        }

        public void MutateOutgoing(LogicalMessage message, TransportMessage transportMessage)
        {

            // Insert our command headers into all messages sent by bus this unit of work
            foreach (var header in WorkHeaders)
                transportMessage.Headers[header.Key] = header.Value;
        }
        public void MutateIncoming(TransportMessage transportMessage)
        {
            var headers = transportMessage.Headers;

            // There are certain headers that we can make note of 
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach( var header in _carryOverHeaders){
                var defaultHeader = "<NOT FOUND>";
                headers.TryGetValue(header, out defaultHeader);

                var workHeader = "Originating." + header;
                WorkHeaders[workHeader] = defaultHeader;
            }

            
            // Copy any application headers the user might have included
            var userHeaders = headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                WorkHeaders[header] = headers[header];
                
        }

    }
}
