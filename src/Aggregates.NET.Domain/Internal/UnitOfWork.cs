using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.MessageMutator;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Unicast.Messages;
using NServiceBus.UnitOfWork;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class UnitOfWork : IUnitOfWork, ICommandUnitOfWork, IEventUnitOfWork, IEventMutator, IMutateTransportMessages, IMessageMutator
    {
        public static String PrefixHeader = "Originating";
        public static String NotFound = "<NOT FOUND>";


        private static readonly ILog Logger = LogManager.GetLogger(typeof(UnitOfWork));
        private readonly IRepositoryFactory _repoFactory;
        private readonly IMessageMapper _mapper;

        private bool _disposed;
        private IDictionary<Type, IRepository> _repositories;
        private IDictionary<String, IEntityRepository> _entityRepositories;
        private IDictionary<String, IRepository> _pocoRepositories;

        private Meter _commandsMeter = Metric.Meter("Commands", Unit.Commands);
        private Timer _commandsTimer = Metric.Timer("Commands Duration", Unit.Commands);
        private Counter _commandsConcurrent = Metric.Counter("Concurrent Commands", Unit.Commands);
        private Meter _eventsMeter = Metric.Meter("Events", Unit.Commands);
        private Timer _eventsTimer = Metric.Timer("Events Duration", Unit.Commands);
        private Counter _eventsConcurrent = Metric.Counter("Concurrent Events", Unit.Commands);
        private TimerContext _timerContext;

        private Meter _errorsMeter = Metric.Meter("Command Errors", Unit.Errors);
        private Meter _eventErrorsMeter = Metric.Meter("Event Errors", Unit.Errors);

        public IBuilder Builder { get; set; }
        public Int32 Retries { get; set; }

        public UnitOfWork(IRepositoryFactory repoFactory, IMessageMapper mapper)
        {
            _repoFactory = repoFactory;
            _mapper = mapper;
            _repositories = new Dictionary<Type, IRepository>();
            _entityRepositories = new Dictionary<String, IEntityRepository>();
            _pocoRepositories = new Dictionary<String, IRepository>();
            CurrentHeaders = new Dictionary<String, String>();
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
                foreach (var repo in _repositories.Values)
                {
                    repo.Dispose();
                }

                _repositories.Clear();
            }
            lock (_entityRepositories)
            {
                foreach (var repo in _entityRepositories.Values)
                {
                    repo.Dispose();
                }

                _entityRepositories.Clear();
            }
            lock (_pocoRepositories)
            {
                foreach (var repo in _pocoRepositories.Values)
                {
                    repo.Dispose();
                }

                _pocoRepositories.Clear();
            }
            _disposed = true;
        }

        public IRepository<T> For<T>() where T : class, IAggregate
        {
            Logger.WriteFormat(LogLevel.Debug, "Retreiving repository for type {0}", typeof(T));
            var type = typeof(T);

            IRepository repository;
            if (!_repositories.TryGetValue(type, out repository))
            {
                repository = (IRepository)_repoFactory.ForAggregate<T>(Builder);
                lock(_repositories) _repositories[type] = repository;
            }
            return (IRepository<T>)repository;
        }
        public IEntityRepository<TParent, TParentId, TEntity> For<TParent, TParentId, TEntity>(TParent parent) where TEntity : class, IEntity where TParent : class, IBase<TParentId>
        {
            Logger.WriteFormat(LogLevel.Debug, "Retreiving entity repository for type {0}", typeof(TEntity));
            var key = $"{parent.StreamId}:{typeof(TEntity).FullName}";

            IEntityRepository repository;
            if (_entityRepositories.TryGetValue(key, out repository))
                return (IEntityRepository<TParent, TParentId, TEntity>)repository;

            return (IEntityRepository<TParent, TParentId, TEntity>)(_entityRepositories[key] = (IEntityRepository)_repoFactory.ForEntity<TParent, TParentId, TEntity>(parent, Builder));
        }
        public IPocoRepository<T> Poco<T>() where T : class, new()
        {
            Logger.WriteFormat(LogLevel.Debug, "Retreiving poco repository for type {0}", typeof(T));
            var key = $"{typeof(T).FullName}";

            IRepository repository;
            if (!_pocoRepositories.TryGetValue(key, out repository))
            {
                repository = (IRepository)_repoFactory.ForPoco<T>();
                lock (_repositories) _pocoRepositories[key] = repository;
            }
            return (IPocoRepository<T>)repository;
        }
        public IPocoRepository<TParent, TParentId, T> Poco<TParent, TParentId, T>(TParent parent) where T : class, new() where TParent : class, IBase<TParentId>
        {
            Logger.WriteFormat(LogLevel.Debug, "Retreiving poco repository for type {0}", typeof(T));
            var key = $"{parent.StreamId}:{typeof(T).FullName}";

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository))
                return (IPocoRepository<TParent, TParentId, T>)repository;

            return (IPocoRepository<TParent, TParentId, T>)(_pocoRepositories[key] = (IRepository)_repoFactory.ForPoco<TParent, TParentId, T>(parent));
        }
        public Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var processor = Builder.Build<IProcessor>();
            return processor.Process<TQuery, TResponse>(Builder, query);
        }
        public Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var result = _mapper.CreateInstance(query);
            return Query<TQuery, TResponse>(result);
        }
        public Task<TResponse> Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>
        {
            var processor = Builder.Build<IProcessor>();
            return processor.Compute<TComputed, TResponse>(Builder, computed);
        }
        public Task<TResponse> Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>
        {
            var result = _mapper.CreateInstance(computed);
            return Compute<TComputed, TResponse>(result);
        }

        Task ICommandUnitOfWork.Begin()
        {
            _commandsMeter.Mark();
            _commandsConcurrent.Increment();
            _timerContext = _commandsTimer.NewContext();
            return Task.FromResult(true);
        }

        async Task ICommandUnitOfWork.End(Exception ex)
        {
            if (ex == null)
                await Commit();
            else
                _errorsMeter.Mark();

            _commandsConcurrent.Decrement();
            _timerContext.Dispose();
        }

        Task IEventUnitOfWork.Begin()
        {
            _eventsMeter.Mark();
            _eventsConcurrent.Increment();
            _timerContext = _eventsTimer.NewContext();
            return Task.FromResult(true);
        }
        async Task IEventUnitOfWork.End(Exception ex)
        {
            if (ex == null)
                await Commit();
            else
                _errorsMeter.Mark();

            _eventsConcurrent.Decrement();
            _timerContext.Dispose();
        }

        private async Task Commit()
        {

            var commitId = Guid.NewGuid();
            String messageId;

            try
            {
                // Attempt to get MessageId from NServicebus headers
                // If we maintain a good CommitId convention it should solve the message idempotentcy issue (assuming the storage they choose supports it)
                if (CurrentHeaders.TryGetValue(Defaults.MessageIdHeader, out messageId))
                    commitId = Guid.Parse(messageId);

                // Allow the user to send a CommitId along with his message if he wants
                if (CurrentHeaders.TryGetValue(Defaults.CommitIdHeader, out messageId))
                    commitId = Guid.Parse(messageId);
            }
            catch (FormatException) { }

            // Insert all command headers into the commit
            var headers = new Dictionary<String, String>(CurrentHeaders);

            Logger.WriteFormat(LogLevel.Debug, "Starting commit id {0}", commitId);
            var aggs = _repositories.Values.WhenAllAsync(async (repo) =>
            {
                try
                {
                    await repo.Commit(commitId, headers);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            });
            var entities = _entityRepositories.Values.WhenAllAsync(async (repo) =>
            {
                try
                {
                    await repo.Commit(commitId, headers);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            });
            var pocos = _pocoRepositories.Values.WhenAllAsync(async (repo) =>
            {
                try
                {
                    await repo.Commit(commitId, headers);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            });
            await Task.WhenAll(aggs, entities, pocos);
            Logger.WriteFormat(LogLevel.Debug, "Commit id {0} complete", commitId);
        }

        public void MutateOutgoing(LogicalMessage message, TransportMessage transportMessage)
        {
            // Insert our command headers into all messages sent by bus this unit of work
            foreach (var header in CurrentHeaders)
                transportMessage.Headers[header.Key] = header.Value.ToString();
        }

        public void MutateIncoming(TransportMessage transportMessage)
        {
            var headers = transportMessage.Headers;

            // There are certain headers that we can make note of
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach (var header in Defaults.CarryOverHeaders)
            {
                var defaultHeader = "";
                headers.TryGetValue(header, out defaultHeader);

                if (String.IsNullOrEmpty(defaultHeader))
                    defaultHeader = NotFound;

                var workHeader = String.Format("{0}.{1}", PrefixHeader, header);
                CurrentHeaders[workHeader] = defaultHeader;
            }

            // Copy any application headers the user might have included
            var userHeaders = headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.CommitIdHeader, StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                CurrentHeaders[header] = headers[header];
        }

        public Object MutateOutgoing(Object message)
        {
            return message;
        }
        public Object MutateIncoming(Object message)
        {
            this.CurrentMessage = message;

            CurrentHeaders[Defaults.DomainHeader] = Defaults.Domain.ToString();
            
            return message;
        }

        // Event mutating
        public Object MutateIncoming(Object Event, IEventDescriptor Descriptor, long? Position)
        {
            this.CurrentMessage = Event;
            CurrentHeaders[Defaults.DomainHeader] = Defaults.Domain.ToString();

            if (Descriptor == null) return Event; 

            var headers = Descriptor.Headers;

            // There are certain headers that we can make note of
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach (var header in Defaults.CarryOverHeaders)
            {
                String defaultHeader;
                if (!headers.TryGetValue(header, out defaultHeader))
                    defaultHeader = NotFound;
                
                var workHeader = String.Format("{0}.{1}", PrefixHeader, header);
                CurrentHeaders[workHeader] = defaultHeader;
            }

            // Copy any application headers the user might have included
            var userHeaders = headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                CurrentHeaders[header] = headers[header];

            return Event;
        }

        public IWritableEvent MutateOutgoing(IWritableEvent Event)
        {
            foreach (var header in CurrentHeaders)
                Event.Descriptor.Headers[header.Key] = header.Value.ToString();
            return Event;
        }

        public Object CurrentMessage { get; private set; }
        public IDictionary<String, String> CurrentHeaders { get; private set; }
    }
}