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

        private static readonly Metrics.Timer PrepareTime = Metric.Timer("UOW Prepare Time", Unit.Items, tags: "debug");
        private static readonly Metrics.Timer CommitTime = Metric.Timer("UOW Commit Time", Unit.Items, tags: "debug");

        private const string CommitHeader = "CommitId";
        private const string TerminatingEventIdHeader = "FinalEventId";
        public static string PrefixHeader = "Originating";
        public static string NotFound = "<NOT FOUND>";

        private static readonly ILog Logger = LogManager.GetLogger("UnitOfWork");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");
        private readonly IRepositoryFactory _repoFactory;
        private readonly IMessageMapper _mapper;

        private bool _disposed;
        private readonly IDictionary<string, IRepository> _repositories;
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

        public IRepository<T> For<T>() where T : Aggregate<T>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving repository for type {typeof(T)}");
            var key = typeof(T).FullName;

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository)) return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[key] = (IRepository)_repoFactory.ForAggregate<T>(Builder));
        }
        public IRepository<TParent, TEntity> For<TParent, TEntity>(TParent parent) where TEntity : Entity<TEntity, TParent> where TParent : Entity<TParent>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity repository for type {typeof(TEntity)}");
            var key = $"{typeof(TParent).FullName}.{typeof(TEntity).FullName}";

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository))
                return (IRepository<TParent, TEntity>)repository;

            return (IRepository<TParent, TEntity>)(_repositories[key] = (IRepository)_repoFactory.ForEntity<TParent, TEntity>(parent, Builder));
        }
        public IPocoRepository<T> Poco<T>() where T : class, new()
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving poco repository for type {typeof(T)}");
            var key = typeof(T).FullName;

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository)) return (IPocoRepository<T>)repository;

            return (IPocoRepository<T>)(_pocoRepositories[key] = (IRepository)_repoFactory.ForPoco<T>(Builder));
        }
        public IPocoRepository<TParent, T> Poco<TParent, T>(TParent parent) where T : class, new() where TParent : Entity<TParent>
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving child poco repository for type {typeof(T)}");
            var key = $"{typeof(TParent).FullName}.{typeof(T).FullName}";

            IRepository repository;
            if (_pocoRepositories.TryGetValue(key, out repository))
                return (IPocoRepository<TParent, T>)repository;

            return (IPocoRepository<TParent, T>)(_pocoRepositories[key] = (IRepository)_repoFactory.ForPoco<TParent, T>(parent, Builder));
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
            // Todo: If current message is an event, detect if they've modified any entities and warn them.
            if (ex != null || CurrentMessage is IEvent)
            {
                Guid eventId;
                EventIds.TryRemove(CommitId, out eventId);
                return Task.CompletedTask;
            }

            return Commit();
        }

        private async Task Commit()
        {
            Guid eventId;
            EventIds.TryRemove(CommitId, out eventId);
            var headers = new Dictionary<string, string>
            {
                [CommitHeader] = CommitId.ToString(),
                [TerminatingEventIdHeader] = eventId.ToString()
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
                
                using (PrepareTime.NewContext())
                {
                    // First check all streams read but not modified - if the store has a different version a VersionException will be thrown
                    await allRepos.WhenAllAsync(x => x.Prepare(CommitId)).ConfigureAwait(false);
                }
            }
            
            Logger.Write(LogLevel.Debug, () => $"Starting commit id {CommitId} with {_repositories.Count + _pocoRepositories.Count} tracked repositories");


            using (var ctx = CommitTime.NewContext())
            {
                await allRepos.WhenAllAsync(x => x.Commit(CommitId, headers)).ConfigureAwait(false);

                if(ctx.Elapsed > TimeSpan.FromSeconds(1))
                    SlowLogger.Write(LogLevel.Warn, () => $"Commit id {CommitId} took {ctx.Elapsed.TotalSeconds} seconds!");
                Logger.Write(LogLevel.Debug, () => $"Commit id {CommitId} took {ctx.Elapsed.TotalMilliseconds} ms");
            }

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
            CurrentHeaders[$"{PrefixHeader}.OriginatingType"] = command.GetType().FullName;

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
                CurrentHeaders[$"{PrefixHeader}.{header}"] = command.Headers[header];
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