using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;
using Aggregates.Messages;

namespace Aggregates.Internal
{
    class UnitOfWork : IDomainUnitOfWork
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

        protected const string CommitHeader = "CommitId";
        public static string NotFound = "<NOT FOUND>";

        protected static readonly ILog Logger = LogProvider.GetLogger("UnitOfWork");

        private readonly IRepositoryFactory _repoFactory;
        private readonly IEventFactory _eventFactory;
        private readonly IProcessor _processor;

        private bool _disposed;
        private readonly IDictionary<string, IRepository> _repositories;
        private readonly IDictionary<string, IRepository> _pocoRepositories;

        public int Retries { get; private set; }

        public Guid CommitId { get; protected set; }
        public object CurrentMessage { get; protected set; }
        public IDictionary<string, string> CurrentHeaders { get; protected set; }

        public UnitOfWork(IRepositoryFactory repoFactory, IEventFactory eventFactory, IProcessor processor)
        {
            _repoFactory = repoFactory;
            _eventFactory = eventFactory;
            _processor = processor;
            _repositories = new Dictionary<string, IRepository>();
            _pocoRepositories = new Dictionary<string, IRepository>();
            CurrentHeaders = new Dictionary<string, string>();
        }

        public void Dispose()
        {
            Dispose(true);
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


            foreach (var repo in _pocoRepositories.Values)
            {
                repo.Dispose();
            }

            _pocoRepositories.Clear();

            _disposed = true;
        }

        public IRepository<T> For<T>() where T : IEntity
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving repository for type {typeof(T)}");
            var key = typeof(T).FullName;

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository)) return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[key] = (IRepository)_repoFactory.ForEntity<T>());
        }
        public IRepository<TParent, TEntity> For<TParent, TEntity>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IEntity
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity repository for type {typeof(TEntity)}");
            var key = $"{typeof(TParent).FullName}.{typeof(TEntity).FullName}";

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository))
                return (IRepository<TParent, TEntity>)repository;

            return (IRepository<TParent, TEntity>)(_repositories[key] = (IRepository)_repoFactory.ForEntity<TParent, TEntity>(parent));
        }
        public IPocoRepository<T> Poco<T>() where T : class, new()
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving poco repository for type {typeof(T)}");
            var key = typeof(T).FullName;

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository)) return (IPocoRepository<T>)repository;

            return (IPocoRepository<T>)(_pocoRepositories[key] = (IRepository)_repoFactory.ForPoco<T>());
        }
        public IPocoRepository<TParent, T> Poco<TParent, T>(TParent parent) where T : class, new() where TParent : IEntity
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving child poco repository for type {typeof(T)}");
            var key = $"{typeof(TParent).FullName}.{typeof(T).FullName}";

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository))
                return (IPocoRepository<TParent, T>)repository;

            return (IPocoRepository<TParent, T>)(_pocoRepositories[key] = (IRepository)_repoFactory.ForPoco<TParent, T>(parent));
        }
        public Task<TResponse> Query<TQuery, TResponse>(TQuery query, IUnitOfWork uow) where TQuery : IQuery<TResponse>
        {
            return _processor.Process<TQuery, TResponse>(query, uow);
        }
        public Task<TResponse> Query<TQuery, TResponse>(Action<TQuery> query, IUnitOfWork uow) where TQuery : IQuery<TResponse>
        {
            var result = _eventFactory.Create(query);
            return Query<TQuery, TResponse>(result, uow);
        }


        Task IUnitOfWork.Begin()
        {
            return Task.FromResult(true);
        }
        Task IUnitOfWork.End(Exception ex)
        {
            // Todo: If current message is an event, detect if they've modified any entities and warn them.
            if (ex != null || CurrentMessage is IEvent)
            {
                Guid eventId;
                EventIds.TryRemove(CommitId, out eventId);
                Retries++;
                return Task.CompletedTask;
            }

            return Commit();
        }

        private async Task Commit()
        {

            var headers = new Dictionary<string, string>
            {
                [CommitHeader] = CommitId.ToString(),
                // Todo: what else can we put in here?
            };

            var allRepos =
                _repositories.Values.Concat(_pocoRepositories.Values).ToArray();


            var changedStreams = _repositories.Sum(x => x.Value.ChangedStreams) + _pocoRepositories.Sum(x => x.Value.ChangedStreams);

            Logger.Write(LogLevel.Debug, () => $"Detected {changedStreams} changed streams in commit {CommitId}");
            // Only prepare if multiple changed streams, which will quickly check all changed streams to see if they are all the same version as when we read them
            // Not 100% guarenteed to eliminate writing 1 stream then failing the other one but will help - and we also tell the user to not do this.. 
            if (changedStreams > 1)
            {
                Logger.Write(LogLevel.Warn, $"Starting prepare for commit id {CommitId} with {_repositories.Count + _pocoRepositories.Count} tracked repositories. You changed {changedStreams} streams.  We highly discourage this https://github.com/volak/Aggregates.NET/wiki/Changing-Multiple-Streams");

                // First check all streams read but not modified - if the store has a different version a VersionException will be thrown
                await allRepos.WhenAllAsync(x => x.Prepare(CommitId)).ConfigureAwait(false);
            }

            Logger.Write(LogLevel.Debug, () => $"Starting commit id {CommitId} with {_repositories.Count + _pocoRepositories.Count} tracked repositories");

            try
            {
                await allRepos.WhenAllAsync(x => x.Commit(CommitId, headers)).ConfigureAwait(false);
            }
            finally
            {
                Guid eventId;
                EventIds.TryRemove(CommitId, out eventId);
            }

        }



    }
}