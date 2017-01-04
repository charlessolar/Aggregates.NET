using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class PocoRepository<TParent, TParentId, T> : PocoRepository<T>, IPocoRepository<TParent, TParentId, T> where TParent : class, IBase<TParentId> where T : class, new()
    {
        private static readonly ILog Logger = LogManager.GetLogger("PocoRepository");

        private readonly TParent _parent;

        public PocoRepository(TParent parent, IBuilder builder) : base(builder)
        { 
            _parent = parent;
        }
        public override Task<T> TryGet<TId>(TId id)
        {
            if (id == null) return null;
            if (typeof(TId) == typeof(string) && string.IsNullOrEmpty(id as string)) return null;
            try
            {
                return Get(id);
            }
            catch (NotFoundException) { }
            return null;
        }

        public override async Task<T> Get<TId>(TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{id}] from parent {_parent.StreamId} [{typeof(TParent).FullName}] in store");
            var streamId = $"{_parent.StreamId}.{id}";

            var entity = await Get(_parent.Bucket, streamId).ConfigureAwait(false);
            return entity;
        }

        public override async Task<T> New<TId>(TId id)
        {
            var streamId = $"{_parent.StreamId}.{id}";

            var entity = await New(_parent.Bucket, streamId).ConfigureAwait(false);
            return entity;
        }
    }

    class PocoRepository<T> : IPocoRepository<T> where T : class, new()
    {
        private static readonly ILog Logger = LogManager.GetLogger("PocoRepository");
        private readonly IStorePocos _store;

        private static readonly Histogram WrittenEvents = Metric.Histogram("Written Pocos", Unit.Events);
        private static readonly Meter WriteErrors = Metric.Meter("Poco Write Errors", Unit.Errors);

        protected readonly IDictionary<Tuple<string, string>, T> Tracked = new Dictionary<Tuple<string, string>, T>();

        private bool _disposed;

        public int TotalUncommitted => Tracked.Count;
        public int ChangedStreams => Tracked.Count;

        public PocoRepository(IBuilder builder)
        {
            _store = builder.Build<IStorePocos>();
        }

        Task IRepository.Prepare(Guid commitId)
        {
            return Task.CompletedTask;
        }

        async Task IRepository.Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            var written = 0;

            await Tracked
                .ToArray()
                .StartEachAsync(3, async tracked =>
            {
                var headers = new Dictionary<string, string>(commitHeaders);

                Interlocked.Add(ref written, 1);

                var count = 0;
                var success = false;
                do
                {
                    try
                    {
                        await _store.Write(tracked.Value, tracked.Key.Item1, tracked.Key.Item2, headers).ConfigureAwait(false);
                        success = true;
                    }
                    catch (PersistenceException e)
                    {
                        WriteErrors.Mark();
                        Logger.WriteFormat(LogLevel.Warn, "Failed to commit events to store for stream: [{0}] bucket [{1}]\nException: {2}", tracked.Key.Item2, tracked.Key.Item1, e.Message);
                    }
                    catch
                    {
                        WriteErrors.Mark();
                        throw;
                    }
                    if (!success)
                    {
                        count++;
                        Thread.Sleep(75 * (count / 2));
                    }
                } while (!success && count < 5);

            }).ConfigureAwait(false);
            WrittenEvents.Update(written);

        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            Tracked.Clear();

            _disposed = true;
        }

        public virtual Task<T> TryGet<TId>(TId id)
        {
            try
            {
                return Get(id);
            }
            catch (NotFoundException) { }
            return null;
        }
        public Task<T> TryGet<TId>(string bucket, TId id)
        {
            try
            {
                return Get(bucket, id);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get<TId>(TId id)
        {
            return Get(Defaults.Bucket, id);
        }

        public async Task<T> Get<TId>(string bucket, TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving aggregate id [{id}] from bucket [{bucket}] in store");
            var root = await Get(bucket, id.ToString()).ConfigureAwait(false);

            return root;
        }
        public async Task<T> Get(string bucket, string id)
        {
            var cacheId = new Tuple<string, string>(bucket, id);
            T root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await _store.Get<T>(bucket, id).ConfigureAwait(false);

            return root;
        }
        
        public virtual Task<T> New<TId>(TId id)
        {
            return New(Defaults.Bucket, id);
        }

        public async Task<T> New<TId>(string bucket, TId id)
        {
            var root = await New(bucket, id.ToString()).ConfigureAwait(false);
            
            return root;
        }
        public Task<T> New(string bucket, string streamId)
        {
            T root;
            var cacheId = new Tuple<string, string>(bucket, streamId);
            Tracked[cacheId] = root = new T();

            return Task.FromResult(root);
        }
    }
}
