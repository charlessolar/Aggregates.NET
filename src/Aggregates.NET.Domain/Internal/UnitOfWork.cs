using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.MessageMutator;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    public class UnitOfWork : IUnitOfWork, ICommandUnitOfWork, IEventUnitOfWork, IMutateOutgoingMessages, IMutateIncomingMessages
    {
        public static string PrefixHeader = "Originating";
        public static string NotFound = "<NOT FOUND>";


        private static readonly ILog Logger = LogManager.GetLogger(typeof(UnitOfWork));
        private readonly IRepositoryFactory _repoFactory;
        private readonly IMessageMapper _mapper;

        private bool _disposed;
        private IDictionary<Type, IRepository> _repositories;
        private IDictionary<string, IEntityRepository> _entityRepositories;
        private IDictionary<string, IRepository> _pocoRepositories;

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
        public int Retries { get; set; }

        public Guid CommitId { get; private set; }
        public object CurrentMessage { get; private set; }
        public IDictionary<string, string> CurrentHeaders { get; private set; }

        public UnitOfWork(IRepositoryFactory repoFactory, IMessageMapper mapper)
        {
            _repoFactory = repoFactory;
            _mapper = mapper;
            _repositories = new Dictionary<Type, IRepository>();
            _entityRepositories = new Dictionary<string, IEntityRepository>();
            _pocoRepositories = new Dictionary<string, IRepository>();
            CurrentHeaders = new Dictionary<string, string>();
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
            
                foreach (var repo in _repositories.Values)
                {
                    repo.Dispose();
                }

                _repositories.Clear();

                foreach (var repo in _entityRepositories.Values)
                {
                    repo.Dispose();
                }

                _entityRepositories.Clear();

                foreach (var repo in _pocoRepositories.Values)
                {
                    repo.Dispose();
                }

                _pocoRepositories.Clear();
            
            _disposed = true;
        }

        public IRepository<T> For<T>() where T : class, IAggregate
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving repository for type {typeof(T)}");
            var type = typeof(T);

            IRepository repository;
            if (_repositories.TryGetValue(type, out repository)) return (IRepository<T>) repository;
            
            return (IRepository<T>)(_repositories[type] = _repoFactory.ForAggregate<T>(Builder));
        }
        public IEntityRepository<TParent, TParentId, TEntity> For<TParent, TParentId, TEntity>(TParent parent) where TEntity : class, IEntity where TParent : class, IBase<TParentId>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity repository for type {typeof(TEntity)}" );
            var key = $"{parent.StreamId}:{typeof(TEntity).FullName}";

            IEntityRepository repository;
            if (_entityRepositories.TryGetValue(key, out repository))
                return (IEntityRepository<TParent, TParentId, TEntity>)repository;
            
            return (IEntityRepository<TParent, TParentId, TEntity>)(_entityRepositories[key] = _repoFactory.ForEntity<TParent, TParentId, TEntity>(parent, Builder));
        }
        public IPocoRepository<T> Poco<T>() where T : class, new()
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving poco repository for type {typeof(T)}");
            var key = $"{typeof(T).FullName}";

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository)) return (IPocoRepository<T>) repository;
            
            return (IPocoRepository<T>)(_pocoRepositories[key] = _repoFactory.ForPoco<T>());
        }
        public IPocoRepository<TParent, TParentId, T> Poco<TParent, TParentId, T>(TParent parent) where T : class, new() where TParent : class, IBase<TParentId>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving poco repository for type {typeof(T)}");
            var key = $"{parent.StreamId}:{typeof(T).FullName}";

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository))
                return (IPocoRepository<TParent, TParentId, T>)repository;

            return (IPocoRepository<TParent, TParentId, T>)(_pocoRepositories[key] = _repoFactory.ForPoco<TParent, TParentId, T>(parent));
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
                await Commit().ConfigureAwait(false);
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
                await Commit().ConfigureAwait(false);
            else
                _errorsMeter.Mark();

            _eventsConcurrent.Decrement();
            _timerContext.Dispose();
        }

        private async Task Commit()
        {


            // Insert all command headers into the commit
            var headers = new Dictionary<string, string>(CurrentHeaders);

            Logger.Write(LogLevel.Debug, () => $"Starting commit id {CommitId}");

            var startingEventId = CommitId;
            var aggs = _repositories.Values.WhenAllAsync(async repo =>
            {
                try
                {
                    startingEventId = await repo.Commit(CommitId, startingEventId, headers).ConfigureAwait(false);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            });
            var entities = _entityRepositories.Values.WhenAllAsync(async repo =>
            {
                try
                {
                    startingEventId = await repo.Commit(CommitId, startingEventId, headers).ConfigureAwait(false);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            });
            var pocos = _pocoRepositories.Values.WhenAllAsync(async repo =>
            {
                try
                {
                    startingEventId = await repo.Commit(CommitId, startingEventId, headers).ConfigureAwait(false);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            });
            await Task.WhenAll(aggs, entities, pocos).ConfigureAwait(false);
            Logger.Write(LogLevel.Debug, () => $"Commit id {CommitId} complete");
        }

        public Task MutateOutgoing(MutateOutgoingMessageContext context)
        {
            var headers = context.OutgoingHeaders;
            foreach (var header in CurrentHeaders)
                headers[header.Key] = header.Value;


            return Task.CompletedTask;
        }

        public Task MutateIncoming(MutateIncomingMessageContext context)
        {
            CurrentMessage = context.Message;

            var headers = context.Headers;

            // There are certain headers that we can make note of
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach (var header in Defaults.CarryOverHeaders)
            {
                var defaultHeader = "";
                headers.TryGetValue(header, out defaultHeader);

                if (string.IsNullOrEmpty(defaultHeader))
                    defaultHeader = NotFound;

                var workHeader = $"{PrefixHeader}.{header}";
                CurrentHeaders[workHeader] = defaultHeader;
            }

            // Copy any application headers the user might have included
            var userHeaders = headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.CommitIdHeader, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.RequestResponse, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.Attempts, StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                CurrentHeaders[header] = headers[header];
            CurrentHeaders[Defaults.InstanceHeader] = Defaults.Instance.ToString();


            CommitId = Guid.NewGuid();

            try
            {
                string messageId;
                // Attempt to get MessageId from NServicebus headers
                // If we maintain a good CommitId convention it should solve the message idempotentcy issue (assuming the storage they choose supports it)
                if (CurrentHeaders.TryGetValue(Defaults.MessageIdHeader, out messageId))
                    CommitId = Guid.Parse(messageId);

                // Allow the user to send a CommitId along with his message if he wants
                if (CurrentHeaders.TryGetValue(Defaults.CommitIdHeader, out messageId))
                    CommitId = Guid.Parse(messageId);
            }
            catch (FormatException) { }

            return Task.CompletedTask;
        }
        
    }
}