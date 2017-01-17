using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.MessageMutator;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class UnitOfWork : IUnitOfWork, IApplicationUnitOfWork
    {
        private static readonly ConcurrentDictionary<Guid, Guid> EventIds = new ConcurrentDictionary<Guid, Guid>();

        public static Guid NextEventId(Guid commitId)
        {
            // Todo: if we are to set the eventid here its important that an event is processed in the same order every retry
            // - Conflict resolution? 
            // - Bulk invokes?
            // (use context bag for above?)
            return EventIds.AddOrUpdate(commitId, commitId, (key, value) => value.Increment());
        }

        private static readonly Metrics.Timer CommitTime = Metric.Timer("UOW Commit Time", Unit.Items);
        public static string PrefixHeader = "Originating";
        public static string NotFound = "<NOT FOUND>";

        private static readonly ILog Logger = LogManager.GetLogger("UnitOfWork");
        private readonly IRepositoryFactory _repoFactory;
        private readonly IMessageMapper _mapper;

        private bool _disposed;
        private readonly IDictionary<Type, IRepository> _repositories;
        private readonly IDictionary<string, IEntityRepository> _entityRepositories;
        private readonly IDictionary<string, IRepository> _pocoRepositories;
        
        public IBuilder Builder { get; set; }
        public int Retries { get; set; }
        public ContextBag Bag { get; set; }

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
            if (_repositories.TryGetValue(type, out repository)) return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[type] = _repoFactory.ForAggregate<T>(Builder));
        }
        public IEntityRepository<TParent, TParentId, TEntity> For<TParent, TParentId, TEntity>(TParent parent) where TEntity : class, IEntity where TParent : class, IBase<TParentId>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity repository for type {typeof(TEntity)}");
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
            if (_pocoRepositories.TryGetValue(key, out repository)) return (IPocoRepository<T>)repository;

            return (IPocoRepository<T>)(_pocoRepositories[key] = _repoFactory.ForPoco<T>(Builder));
        }
        public IPocoRepository<TParent, TParentId, T> Poco<TParent, TParentId, T>(TParent parent) where T : class, new() where TParent : class, IBase<TParentId>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving child poco repository for type {typeof(T)}");
            var key = $"{parent.StreamId}:{typeof(T).FullName}";

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository))
                return (IPocoRepository<TParent, TParentId, T>)repository;

            return (IPocoRepository<TParent, TParentId, T>)(_pocoRepositories[key] = _repoFactory.ForPoco<TParent, TParentId, T>(parent, Builder));
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


        Task IApplicationUnitOfWork.Begin()
        {
            return Task.FromResult(true);
        }
        Task IApplicationUnitOfWork.End(Exception ex)
        {
            Guid eventId;
            EventIds.TryRemove(CommitId, out eventId);
            
            // Todo: If current message is an event, detect if they've modified any entities and warn them.
            if (ex != null || CurrentMessage is IEvent)
                return Task.CompletedTask;

            return Commit();
        }

        private async Task Commit()
        {
            // Insert all command headers into the commit
            var headers = new Dictionary<string, string>(CurrentHeaders);
            
            using (CommitTime.NewContext())
            {

                var allRepos = _repositories.Values.Concat(_entityRepositories.Values).Concat(_pocoRepositories.Values).ToArray();

                Logger.Write(LogLevel.Debug, () =>
                        $"Starting prepare for commit id {CommitId} with {_repositories.Count + _entityRepositories.Count + _pocoRepositories.Count} tracked repositories");
                // First check all streams read but not modified - if the store has a different version a VersionException will be thrown
                await allRepos.StartEachAsync(3, (x) => x.Prepare(CommitId)).ConfigureAwait(false);

                // this log message can be expensive as the list is computed for a check
                // so only warn users about multiple stream commits when debugging
                Logger.Write(LogLevel.Debug, () =>
                {
                    var orderedRepos = _repositories.Select(x => new Tuple<int, IRepository>(x.Value.ChangedStreams, x.Value))
                                                    .Concat(_entityRepositories.Select(x => new Tuple<int, IRepository>(x.Value.ChangedStreams, x.Value)));
                    if (orderedRepos.Count(x => x.Item1 != 0) > 1)
                        return
                             $"Starting commit id {CommitId} with {_repositories.Count + _entityRepositories.Count + _pocoRepositories.Count} tracked repositories. You changed {orderedRepos.Sum(x => x.Item1)} streams.  We highly discourage this https://github.com/volak/Aggregates.NET/wiki/Changing-Multiple-Streams";

                    return
                            $"Starting commit id {CommitId} with {_repositories.Count + _entityRepositories.Count + _pocoRepositories.Count} tracked repositories";
                });

                await allRepos.StartEachAsync(3, (x) => x.Commit(CommitId, headers)).ConfigureAwait(false);
                
            }
            Logger.Write(LogLevel.Debug, () => $"Commit id {CommitId} complete");

        }

        public IMutating MutateIncoming(IMutating command)
        {
            CurrentMessage = command.Message;

            // There are certain headers that we can make note of
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach (var header in Defaults.CarryOverHeaders)
            {
                var defaultHeader = "";
                command.Headers.TryGetValue(header, out defaultHeader);

                if (string.IsNullOrEmpty(defaultHeader))
                    defaultHeader = NotFound;

                var workHeader = $"{PrefixHeader}.{header}";
                CurrentHeaders[workHeader] = defaultHeader;
            }

            // Copy any application headers the user might have included
            var userHeaders = command.Headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.CommitIdHeader, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.RequestResponse, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.Retries, StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                CurrentHeaders[header] = command.Headers[header];
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
            
            return command;
        }
        public IMutating MutateOutgoing(IMutating command)
        {
            foreach (var header in CurrentHeaders)
                command.Headers[header.Key] = header.Value;

            return command;
        }
        

    }
}