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
    public class UnitOfWork : Aggregates.UnitOfWork.IDomain, Aggregates.UnitOfWork.IUnitOfWork, IDisposable
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

        protected const string NotFound = "<NOT FOUND>";

        internal static readonly ILog Logger = LogProvider.GetLogger("UnitOfWork");

        private readonly IRepositoryFactory _repoFactory;
        private readonly IEventFactory _eventFactory;

        private bool _disposed;
        private readonly IDictionary<string, IRepository> _repositories;
        
        public Guid CommitId { get; internal set; }
        public object CurrentMessage { get; internal set; }
        public IDictionary<string, string> CurrentHeaders { get; internal set; }

        public UnitOfWork(IRepositoryFactory repoFactory, IEventFactory eventFactory)
        {
            _repoFactory = repoFactory;
            _eventFactory = eventFactory;
            _repositories = new Dictionary<string, IRepository>();
            CurrentHeaders = new Dictionary<string, string>();
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            foreach (var repo in _repositories.Values)
            {
                repo.Dispose();
            }

            _repositories.Clear();

            _disposed = true;
        }

        public IRepository<T> For<T>() where T : IEntity
        {
            var key = typeof(T).FullName;

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository)) return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[key] = (IRepository)_repoFactory.ForEntity<T>());
        }
        public IRepository<TEntity, TParent> For<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            var key = $"{typeof(TParent).FullName}.{parent.Id}.{typeof(TEntity).FullName}";

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository))
                return (IRepository<TEntity, TParent>)repository;

            return (IRepository<TEntity, TParent>)(_repositories[key] = (IRepository)_repoFactory.ForEntity<TEntity, TParent>(parent));
        }


        Task Aggregates.UnitOfWork.IUnitOfWork.Begin()
        {
            return Task.FromResult(true);
        }
        Task Aggregates.UnitOfWork.IUnitOfWork.End(Exception ex)
        {
            // Todo: If current message is an event, detect if they've modified any entities and warn them.
            if (ex != null || CurrentMessage is IEvent)
            {
                // On exception Begin and End will run multiple times without a new unit of work instance
                _repositories.Clear();

                Guid eventId;
                EventIds.TryRemove(CommitId, out eventId);
                return Task.CompletedTask;
            }

            return Commit();
        }

        private async Task Commit()
        {
            if (CommitId == Guid.Empty)
                throw new InvalidOperationException("Cannot commit - CommitId cannot be empty. This needs to be a unique GUID id");

            var headers = new Dictionary<string, string>
            {
                [$"{Defaults.PrefixHeader}.{Defaults.CommitIdHeader}"] = CommitId.ToString(),
                [$"{Defaults.PrefixHeader}.Instance"] = Defaults.Instance.ToString()
                // Todo: what else can we put in here?
            };

            var allRepos =
                _repositories.Values.Cast<IRepositoryCommit>().ToArray();

            var changedStreams = allRepos.Sum(x => x.ChangedStreams);
            
            Logger.DebugEvent("Changed", "{Changed} streams {CommitId}", changedStreams, CommitId);
            // Only prepare if multiple changed streams, which will quickly check all changed streams to see if they are all the same version as when we read them
            // Not 100% guarenteed to eliminate writing 1 stream then failing the other one but will help - and we also tell the user to not do this.. 
            if (changedStreams > 1)
            {
                Logger.WarnEvent("BestPractices", "{Changed} changed streams. We highly discourage this https://github.com/volak/Aggregates.NET/wiki/Changing-Multiple-Streams", changedStreams, CommitId);
                // First check all streams read but not modified - if the store has a different version a VersionException will be thrown
                await allRepos.WhenAllAsync(x => x.Prepare(CommitId)).ConfigureAwait(false);
            }
            
            Logger.DebugEvent("Commit", "{CommitId} for {Repositories} repositories", CommitId, allRepos.Length);
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

        public virtual IMutating MutateIncoming(IMutating command)
        {
            return command;
        }
        public virtual IMutating MutateOutgoing(IMutating command)
        {
            return command;
        }
    }
}