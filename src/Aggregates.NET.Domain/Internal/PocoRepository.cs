using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class PocoRepository<TParent, T> : PocoRepository<T>, IPocoRepository<TParent, T> where TParent : Entity<TParent> where T : class, new()
    {
        private static readonly ILog Logger = LogManager.GetLogger("PocoRepository");

        private readonly TParent _parent;

        public PocoRepository(TParent parent, IStorePocos store) : base(store)
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
            var parent = (IEventSourced)_parent;

            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{id}] from parent {_parent.Id} [{typeof(TParent).FullName}] in store");
            var streamId = $"{parent.BuildParentsString()}.{id}";
            
            var entity = await Get(parent.Stream.Bucket, streamId, parent.BuildParents()).ConfigureAwait(false);
            return entity;
        }

        public override async Task<T> New(Id id)
        {
            var parent = (IEventSourced)_parent;

            var streamId = $"{parent.BuildParentsString()}.{id}";

            var entity = await New(parent.Stream.Bucket, streamId, parent.BuildParents()).ConfigureAwait(false);
            return entity;
        }
    }

    class PocoRepository<T> : IPocoRepository<T>, IRepository where T : class, new()
    {
        private static readonly ILog Logger = LogManager.GetLogger("PocoRepository");
        private readonly IStorePocos _store;

        private static readonly Histogram WrittenEvents = Metric.Histogram("Written Pocos", Unit.Events, tags: "debug");
        private static readonly Meter WriteErrors = Metric.Meter("Poco Write Errors", Unit.Errors, tags: "debug");

        protected readonly IDictionary<Tuple<string, Id, IEnumerable<Id>>, Tuple<long, T, string>> Tracked = new Dictionary<Tuple<string, Id, IEnumerable<Id>>, Tuple<long, T, string>>();

        private bool _disposed;

        public int TotalUncommitted => Tracked.Count;

        public int ChangedStreams
        {
            get
            {
                // Compares the stored serialized poco against the current to determine how many changed
                return Tracked.Values.Count(x => x.Item1 == -1 || JsonConvert.SerializeObject(x.Item2) != x.Item3);
            }
        }

        public PocoRepository(IStorePocos store)
        {
            _store = store;
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
                .WhenAllAsync(async tracked =>
            {
                var headers = new Dictionary<string, string>(commitHeaders);

                Interlocked.Add(ref written, 1);

                var count = 0;
                var success = false;
                do
                {
                    var serialized = JsonConvert.SerializeObject(tracked.Value.Item2);
                    // Poco didnt change, no need to save
                    if (tracked.Value.Item1 != -1 && serialized == tracked.Value.Item3)
                    {
                        success = true;
                        break;
                    }

                    try
                    {
                        await _store.Write(new Tuple<long, T>(tracked.Value.Item1, tracked.Value.Item2), tracked.Key.Item1, tracked.Key.Item2, tracked.Key.Item3, headers).ConfigureAwait(false);
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
                        Thread.Sleep(25 * (count / 2));
                    }
                } while (!success && count < 5);

                if (!success)
                    throw new PersistenceException(
                        $"Failed to commit events to store for stream: [{tracked.Key.Item2}] bucket [{tracked.Key.Item1}] after 5 retries");

            }).ConfigureAwait(false);
            WrittenEvents.Update(written);

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
        protected async Task<T> Get(string bucket, Id id, IEnumerable<Id> parents)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving aggregate id [{id}] from bucket [{bucket}] in store");
            var cacheId = new Tuple<string, Id, IEnumerable<Id>>(bucket, id, parents);
            Tuple<long,T, string> root;
            if (Tracked.TryGetValue(cacheId, out root))
                return root.Item2;

            var poco = await _store.Get<T>(bucket, id, parents).ConfigureAwait(false);

            if (poco == null)
                throw new NotFoundException($"Poco {cacheId} not found");

            // Storing the original value in the cache via SerializeObject so we can check if needs saving
            Tracked[cacheId] = new Tuple<long, T, string>(poco.Item1, poco.Item2, JsonConvert.SerializeObject(poco.Item2));

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

        protected Task<T> New(string bucket, Id id, IEnumerable<Id> parents)
        {
            var cacheId = new Tuple<string, Id, IEnumerable<Id>>(bucket, id, parents);

            if (Tracked.ContainsKey(cacheId))
                throw new InvalidOperationException($"Poco of Id {cacheId} already exists, cannot make a new one");

            var poco = new T();

            Tracked[cacheId] = new Tuple<long, T, string>(-1, poco, JsonConvert.SerializeObject(poco));

            return Task.FromResult(poco);
        }
    }
}
