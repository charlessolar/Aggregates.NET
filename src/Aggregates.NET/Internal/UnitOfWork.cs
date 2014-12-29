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

            _eventStore.Dispose();
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
            if( _repositories.TryGetValue(type, out repository) )
                return (IRepository<T>)repository;

            var repoType = typeof(Repository<>).MakeGenericType(typeof(T));
            return (IRepository<T>)(_repositories[type] = (IRepository)Activator.CreateInstance(repoType, _builder, _eventStore));
        }



        public void Commit()
        {
            var commitId = Guid.NewGuid();
            
            foreach (var repo in _repositories)
            {
                try
                {
                    var headers = new Dictionary<String, String>(WorkHeaders);
                    headers[AggregateTypeHeader] = repo.Key.FullName;

                    repo.Value.Commit(commitId, headers);
                }
                catch( StorageException e )
                {
                    throw new PersistenceException(e.Message, e);
                }
            }
        }

        public void MutateOutgoing(LogicalMessage message, TransportMessage transportMessage)
        {
            foreach (var header in WorkHeaders)
                transportMessage.Headers[header.Key] = header.Value;
        }
        public void MutateIncoming(TransportMessage transportMessage)
        {
            var headers = transportMessage.Headers;
            foreach (var header in headers)
                WorkHeaders[header.Key] = header.Value;
        }

        public virtual void Dispatch(ICommit commit)
        {
            // After a successful event store commit, we need to publish all the events to NServiceBus
            foreach (var header in commit.Headers)
                WorkHeaders[header.Key] = header.Value.ToString();

            foreach (var @event in commit.Events)
            {
                foreach (var header in @event.Headers)
                    _bus.OutgoingHeaders[header.Key] = header.Value.ToString();

                _bus.Publish(@event.Body);
            }

        }

    }
}