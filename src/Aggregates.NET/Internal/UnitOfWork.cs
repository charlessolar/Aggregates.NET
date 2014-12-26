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

namespace Aggregates.Internal
{

    public class UnitOfWork : IUnitOfWork
    {
        private const string AggregateTypeHeader = "AggregateType";
        private static readonly ILog Logger = LogManager.GetLogger(typeof(UnitOfWork));
        private readonly IContainer _container;
        private readonly IStoreEvents _eventStore;
        private readonly IBus _bus;

        private IDictionary<String, String> _workHeaders;

        private bool _disposed;
        private IDictionary<Type, IRepositoryBase> _repositories;

        public UnitOfWork(IContainer container, IStoreEvents eventStore, IBus bus)
        {
            _container = container;
            _eventStore = eventStore;
            _bus = bus;
            _repositories = new Dictionary<Type, IRepositoryBase>();
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _eventStore.Dispose();
            }
            _disposed = true;
        }

        public IRepository<T> For<T>() where T : class, IEventSourceBase
        {
            Logger.DebugFormat("Retreiving repository for type {0}", typeof(T));
            var type = typeof(T);

            IRepositoryBase repository;
            if( _repositories.TryGetValue(type, out repository) )
                return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[type] = (IRepositoryBase)_container.Build(typeof(IRepository<>)));
        }

        public void Commit()
        {
            var commitId = Guid.NewGuid();
            
            foreach (var repo in _repositories)
            {
                try
                {
                    var headers = new Dictionary<String, String>(_workHeaders);
                    headers[AggregateTypeHeader] = repo.Key.FullName;

                    repo.Value.Commit(commitId, headers);
                }
                catch( StorageException e )
                {
                    throw new PersistenceException(e.Message, e);
                }
            }            
        }

        public void Invoke(IncomingContext context, Action next)
        {
            // Take a copy of NServicebus headers for saving to event store
            var headers = context.PhysicalMessage.Headers;
            _workHeaders = new Dictionary<String, String>(headers);
            next();
        }
        public void Invoke(OutgoingContext context, Action next)
        {
            // Mutate all outgoing messages to include the commit headers from Dispatch
            var headers = context.OutgoingMessage.Headers;

            foreach (var header in _workHeaders)
                headers[header.Key] = header.Value;

            next();
        }

        public virtual void Dispatch(ICommit commit)
        {
            // After a successful event store commit, we need to publish all the events to NServiceBus
            foreach (var header in commit.Headers)
                _workHeaders[header.Key] = header.Value.ToString();

            foreach (var @event in commit.Events)
            {
                foreach (var header in @event.Headers)
                    _bus.SetMessageHeader(@event.Body, header.Key, header.Value.ToString());

                _bus.Publish(@event.Body);
            }

        }

    }
}