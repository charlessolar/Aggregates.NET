using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class PocoRepository<TParent, TParentId, T> : PocoRepository<T>, IPocoRepository<TParent, TParentId, T> where TParent : class, IBase<TParentId> where T : class, new()
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(PocoRepository<,,>));

        private readonly TParent _parent;

        public PocoRepository(TParent parent, IBuilder builder)
            : base(builder)
        {
            _parent = parent;
        }
        public override Task<T> TryGet<TId>(TId id)
        {
            if (id == null) return null;
            if (typeof(TId) == typeof(String) && String.IsNullOrEmpty(id as String)) return null;
            try
            {
                return Get<TId>(id);
            }
            catch (NotFoundException) { }
            return null;
        }

        public override async Task<T> Get<TId>(TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{id}] from parent {_parent.StreamId} [{typeof(TParent).FullName}] in store");
            var streamId = String.Format("{0}.{1}", _parent.StreamId, id);

            var entity = await Get(_parent.Bucket, streamId);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TParent, TParentId>).Parent = _parent;

            return entity;
        }

        public override async Task<T> New<TId>(TId id)
        {
            var streamId = String.Format("{0}.{1}", _parent.StreamId, id);

            var entity = await New(_parent.Bucket, streamId);

            try
            {
                (entity as IEventSource<TId>).Id = id;
                (entity as IEntity<TId, TParent, TParentId>).Parent = _parent;
            }
            catch (NullReferenceException)
            {
                var message = String.Format("Failed to new up entity {0}, could not set parent id! Information we have indicated entity has id type <{1}> with parent id type <{2}> - please review that this is true", typeof(T).FullName, typeof(TId).FullName, typeof(TParentId).FullName);
                Logger.Error(message);
                throw new ArgumentException(message);
            }
            return entity;
        }
    }

    public class PocoRepository<T> : IPocoRepository<T> where T : class, new()
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(PocoRepository<>));
        private readonly IStorePocos _store;
        private readonly IBuilder _builder;

        private static Histogram WrittenEvents = Metric.Histogram("Written Pocos", Unit.Events);
        private static Meter WriteErrors = Metric.Meter("Poco Write Errors", Unit.Errors);

        protected readonly IDictionary<Tuple<String, String>, T> _tracked = new Dictionary<Tuple<String, String>, T>();

        private Boolean _disposed;

        public PocoRepository(IBuilder builder)
        {
            _builder = builder;
            _store = _builder.Build<IStorePocos>();
        }

        async Task IRepository.Commit(Guid commitId, IDictionary<String, String> commitHeaders)
        {
            var written = 0;

            await _tracked.WhenAllAsync(async (tracked) =>
            {
                var headers = new Dictionary<String, String>(commitHeaders);

                Interlocked.Add(ref written, 1);


                var count = 0;
                var success = false;
                do
                {
                    try
                    {
                        await _store.Write<T>(tracked.Value, tracked.Key.Item1, tracked.Key.Item2, headers);
                        success = true;
                    }
                    catch (PersistenceException e)
                    {
                        WriteErrors.Mark();
                        Logger.WriteFormat(LogLevel.Warn, "Failed to commit events to store for stream: [{0}] bucket [{1}]\nException: {2}", tracked.Key.Item2, tracked.Key.Item1, e);
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

            });
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

            _tracked.Clear();

            _disposed = true;
        }

        public virtual Task<T> TryGet<TId>(TId id)
        {
            try
            {
                return Get<TId>(id);
            }
            catch (NotFoundException) { }
            return null;
        }
        public Task<T> TryGet<TId>(String bucket, TId id)
        {
            try
            {
                return Get<TId>(bucket, id);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get<TId>(TId id)
        {
            return Get<TId>(Defaults.Bucket, id);
        }

        public async Task<T> Get<TId>(String bucket, TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving aggregate id [{id}] from bucket [{bucket}] in store");
            var root = await Get(bucket, id.ToString());
            (root as IEventSource<TId>).Id = id;
            return root;
        }
        public async Task<T> Get(String bucket, String id)
        {
            var cacheId = new Tuple<String, String>(bucket, id);
            T root;
            if (!_tracked.TryGetValue(cacheId, out root))
                _tracked[cacheId] = root = await _store.Get<T>(bucket, id);

            return root;
        }
        
        public virtual Task<T> New<TId>(TId id)
        {
            return New<TId>(Defaults.Bucket, id);
        }

        public async Task<T> New<TId>(String bucket, TId id)
        {
            var root = await New(bucket, id.ToString());
            (root as IEventSource<TId>).Id = id;

            return root;
        }
        public Task<T> New(String bucket, String streamId)
        {
            T root;
            var cacheId = new Tuple<String, String>(bucket, streamId);
            _tracked[cacheId] = root = new T();

            return Task.FromResult(root);
        }
    }
}
