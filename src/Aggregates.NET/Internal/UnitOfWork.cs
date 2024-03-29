using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class UnitOfWork : Aggregates.UnitOfWork.IDomainUnitOfWork, Aggregates.UnitOfWork.IBaseUnitOfWork, IDisposable
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

        internal readonly ILogger Logger;

        private readonly IRepositoryFactory _repoFactory;
		protected readonly IVersionRegistrar _registrar;

		private bool _disposed;
        private readonly IDictionary<string, IRepository> _repositories;

        public Guid CommitId { get; internal set; }
        public Guid MessageId { get; internal set; }
        public object CurrentMessage { get; internal set; }
        public IDictionary<string, string> CurrentHeaders { get; internal set; }

        public UnitOfWork(ILogger<UnitOfWork> logger, IRepositoryFactory repoFactory, IVersionRegistrar registrar)
        {
            Logger = logger;
            _repoFactory = repoFactory;
			_registrar = registrar;
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

            EventIds.TryRemove(CommitId, out _);
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


        Task Aggregates.UnitOfWork.IBaseUnitOfWork.Begin()
        {
            return Task.FromResult(true);
        }
        Task Aggregates.UnitOfWork.IBaseUnitOfWork.End(Exception ex)
        {
            // Todo: If current message is an event, detect if they've modified any entities and warn them.
            if (ex != null || CurrentMessage is IEvent)
            {
                // On exception Begin and End will run multiple times without a new unit of work instance
                _repositories.Clear();

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
                Logger.WarnEvent("BestPractices", "{Changed} changed streams. We highly discourage this https://github.com/charlessolar/Aggregates.NET/wiki/Changing-Multiple-Streams", changedStreams, CommitId);
                // First check all streams read but not modified - if the store has a different version a VersionException will be thrown
                await allRepos.WhenAllAsync(x => x.Prepare(CommitId)).ConfigureAwait(false);
            }

            Logger.DebugEvent("Commit", "{CommitId} for {Repositories} repositories", CommitId, allRepos.Length);

            await allRepos.WhenAllAsync(x => x.Commit(CommitId, headers)).ConfigureAwait(false);
        }

        public virtual IMutating MutateIncoming(IMutating command)
        {
            CurrentMessage = command.Message;

            string messageId;
            Guid commitId = Guid.NewGuid();

			CurrentHeaders[Defaults.OriginatingMessageHeader] = CurrentMessage == null ? "<UNKNOWN>" : _registrar.GetVersionedName(CurrentMessage.GetType(), insert: false);

			if (command.Headers.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}", out messageId))
                Guid.TryParse(messageId, out commitId);
			if (command.Headers.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.EventIdHeader}", out messageId))
				Guid.TryParse(messageId, out commitId);


			CommitId = commitId;
            MessageId = commitId;
			CurrentHeaders[Defaults.OriginatingMessageId] = messageId;

			// Helpful log and gets CommitId into the dictionary
			var firstEventId = UnitOfWork.NextEventId(CommitId);
            return command;
        }
        public virtual IMutating MutateOutgoing(IMutating command)
        {
            return command;
        }
    }
}