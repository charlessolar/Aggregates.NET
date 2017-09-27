using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class PocoRepository<T, TParent> : PocoRepository<T>, IPocoRepository<T, TParent> where TParent : IEntity where T : class, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger("PocoRepository");

        private readonly TParent _parent;

        public PocoRepository(TParent parent, IMetrics metrics, IStorePocos store, IMessageSerializer serializer, IDomainUnitOfWork uow) 
            : base(metrics, store, serializer, uow)
        {
            _parent = parent;
        }
        public override Task<T> TryGet(Id id)
        {
            if (id == null) return null;

            try
            {
                return Get(id);
            }
            catch (NotFoundException) { }
            return null;
        }

        public override async Task<T> Get(Id id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving poco id [{id}] from parent {_parent.Id} [{typeof(TParent).FullName}] in store");
            var streamId = $"{_parent.BuildParentsString()}.{id}";

            var entity = await Get(_parent.Bucket, streamId, _parent.BuildParents()).ConfigureAwait(false);
            return entity;
        }

        public override async Task<T> New(Id id)
        {
            var streamId = $"{_parent.BuildParentsString()}.{id}";

            var entity = await New(_parent.Bucket, streamId, _parent.BuildParents()).ConfigureAwait(false);
            return entity;
        }
    }

    class PocoRepository<T> : IPocoRepository<T>, IRepository where T : class, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger("PocoRepository");

        protected readonly IDictionary<Tuple<string, Id, Id[]>, Tuple<long, T, string>> Tracked = new Dictionary<Tuple<string, Id, Id[]>, Tuple<long, T, string>>();

        protected readonly IMetrics _metrics;
        private readonly IStorePocos _store;
        private readonly IMessageSerializer _serializer;
        protected readonly IDomainUnitOfWork _uow;

        private bool _disposed;

        public int ChangedStreams =>
                // Compares the stored serialized poco against the current to determine how many changed
                Tracked.Values.Count(x => x.Item1 == EntityFactory.NewEntityVersion || _serializer.Serialize(x.Item2).AsString() != x.Item3);

        public PocoRepository(IMetrics metrics, IStorePocos store, IMessageSerializer serializer, IDomainUnitOfWork uow)
        {
            _metrics = metrics;
            _store = store;
            _serializer = serializer;
            _uow = uow;
        }

        Task IRepository.Prepare(Guid commitId)
        {
            return Task.CompletedTask;
        }

        async Task IRepository.Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            await Tracked
                .ToArray()
                .WhenAllAsync(async tracked =>
                {
                    var headers = new Dictionary<string, string>(commitHeaders);
                    
                    var serialized = _serializer.Serialize(tracked.Value.Item2).AsString();
                    // Poco didnt change, no need to save
                    if (tracked.Value.Item1 != EntityFactory.NewEntityVersion && serialized == tracked.Value.Item3)
                        return;

                    try
                    {
                        await _store.Write(new Tuple<long, T>(tracked.Value.Item1, tracked.Value.Item2), tracked.Key.Item1, tracked.Key.Item2, tracked.Key.Item3, headers)
                            .ConfigureAwait(false);
                    }
                    catch (PersistenceException e)
                    {
                        Logger.WriteFormat(LogLevel.Warn, "Failed to commit events to store for stream: [{0}] bucket [{1}]\nException: {2}", tracked.Key.Item2, tracked.Key.Item1, e.Message);
                        throw;
                    }

                }).ConfigureAwait(false);

        }


        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            Tracked.Clear();

            _disposed = true;
        }

        public virtual Task<T> TryGet(Id id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<T> TryGet(string bucket, Id id)
        {
            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get(Id id)
        {
            return Get(Defaults.Bucket, id);
        }

        public Task<T> Get(string bucket, Id id)
        {
            return Get(bucket, id, null);
        }
        protected async Task<T> Get(string bucket, Id id, Id[] parents)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving poco id [{id}] from bucket [{bucket}] in store");
            var cacheId = new Tuple<string, Id, Id[]>(bucket, id, parents);
            Tuple<long, T, string> root;
            if (Tracked.TryGetValue(cacheId, out root))
                return root.Item2;

            var poco = await _store.Get<T>(bucket, id, parents).ConfigureAwait(false);

            if (poco == null)
                throw new NotFoundException($"Poco {cacheId} not found");

            // Storing the original value in the cache via SerializeObject so we can check if needs saving
            Tracked[cacheId] = new Tuple<long, T, string>(poco.Item1, poco.Item2, _serializer.Serialize(poco.Item2).AsString());

            return poco.Item2;
        }

        public virtual Task<T> New(Id id)
        {
            return New(Defaults.Bucket, id);
        }

        public Task<T> New(string bucket, Id id)
        {
            return New(bucket, id, null);
        }

        protected Task<T> New(string bucket, Id id, Id[] parents)
        {
            var cacheId = new Tuple<string, Id, Id[]>(bucket, id, parents);

            if (Tracked.ContainsKey(cacheId))
                throw new InvalidOperationException($"Poco of Id {cacheId} already exists, cannot make a new one");

            var poco = new T();

            Tracked[cacheId] = new Tuple<long, T, string>(EntityFactory.NewEntityVersion, poco, _serializer.Serialize(poco).AsString());

            return Task.FromResult(poco);
        }
    }
}
