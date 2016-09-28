using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{

    public class EventStream<T> : IEventStream where T : class, IEventSource
    {
        private static String CommitHeader = "CommitId";
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStream<>));
        public String Bucket { get; private set; }
        public String StreamId { get; private set; }
        public Int32 StreamVersion { get { return this._streamVersion + this._uncommitted.Count; } }
        public Int32 CommitVersion { get { return this._streamVersion; } }

        public IEnumerable<IWritableEvent> Events
        {
            get
            {
                return this._committed.Concat(this._uncommitted);
            }
        }
        public IEnumerable<IWritableEvent> Uncommitted
        {
            get
            {
                return this._uncommitted.Concat(this._outofband);
            }
        }

        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly IOOBHandler _oobHandler;
        private readonly IBuilder _builder;
        private readonly Int32 _streamVersion;
        private IEnumerable<IWritableEvent> _committed;
        private IList<IWritableEvent> _uncommitted;
        private IList<IWritableEvent> _outofband;
        private IList<ISnapshot> _pendingShots;

        public EventStream(IBuilder builder, IStoreEvents store, String bucket, String streamId, Int32 streamVersion, IEnumerable<IWritableEvent> events)
        {
            this._store = store;
            this._snapshots = builder?.Build<IStoreSnapshots>();
            this._oobHandler = builder?.Build<IOOBHandler>();
            this._builder = builder;
            this.Bucket = bucket;
            this.StreamId = streamId;
            this._committed = events.ToList();
            this._streamVersion = streamVersion;
            this._uncommitted = new List<IWritableEvent>();
            this._outofband = new List<IWritableEvent>();
            this._pendingShots = new List<ISnapshot>();

        }

        // Special constructor for building from a cached instance
        internal EventStream(IEventStream clone, IBuilder builder, IStoreEvents store)
        {
            this._store = store;
            this._snapshots = builder.Build<IStoreSnapshots>();
            this._oobHandler = builder.Build<IOOBHandler>();
            this._builder = builder;
            this.Bucket = clone.Bucket;
            this.StreamId = clone.StreamId;
            this._streamVersion = clone.StreamVersion;
            this._committed = clone.Events.ToList();
            this._uncommitted = new List<IWritableEvent>();
            this._outofband = new List<IWritableEvent>();
            this._pendingShots = new List<ISnapshot>();

        }

        /// <summary>
        /// Clones the stream for caching, add an event to the new clone stream optionally
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        public IEventStream Clone(IWritableEvent @event = null)
        {
            var committed = _committed.ToList();
            if (@event != null)
                committed.Add(@event);

            return new EventStream<T>(null, null, Bucket, StreamId, StreamVersion, committed);
        }
        public Task<IEnumerable<IWritableEvent>> AllEvents(Boolean? backwards)
        {
            if (backwards == true)
                return _store.GetEventsBackwards<T>(this.Bucket, this.StreamId);
            return _store.GetEvents<T>(this.Bucket, this.StreamId);
        }
        public Task<IEnumerable<IWritableEvent>> OOBEvents(Boolean? backwards)
        {
            return _oobHandler.Retrieve<T>(this.Bucket, this.StreamId, Ascending: !(backwards ?? false));
        }

        private IWritableEvent makeWritableEvent(IEvent @event, IDictionary<String, String> headers, Boolean version = true)
        {

            IWritableEvent writable = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    Timestamp = DateTime.UtcNow,
                    Version = version ? this.StreamVersion + 1 : this.StreamVersion,
                    Headers = headers
                },
                Event = @event,
                EventId = Guid.NewGuid()
            };

            var mutators = _builder.BuildAll<IEventMutator>();
            if (mutators != null && mutators.Any())
                foreach (var mutate in mutators)
                {
                    Logger.Write(LogLevel.Debug, () => $"Mutating outgoing event {@event.GetType().FullName} with mutator {mutate.GetType().FullName}");
                    writable.Event = mutate.MutateOutgoing(writable.Event);
                }
            return writable;
        }

        public void AddOutOfBand(IEvent @event, IDictionary<String, String> headers)
        {
            _outofband.Add(makeWritableEvent(@event, headers, false));
        }

        public void Add(IEvent @event, IDictionary<String, String> headers)
        {
            _uncommitted.Add(makeWritableEvent(@event, headers));
        }

        public void AddSnapshot(Object memento, IDictionary<String, String> headers)
        {
            this._pendingShots.Add(new Snapshot
            {
                Bucket = this.Bucket,
                Stream = this.StreamId,
                Payload = memento,
                Version = this.StreamVersion,
                EntityType = memento.GetType().AssemblyQualifiedName,
                Timestamp = DateTime.UtcNow,
            });
        }

        public async Task Commit(Guid commitId, IDictionary<String, String> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Event stream [{this.StreamId}] in bucket [{this.Bucket}] for type {typeof(T).FullName} commiting {this.Uncommitted.Count()} events");


            if (commitHeaders == null)
                commitHeaders = new Dictionary<String, String>();

            commitHeaders[CommitHeader] = commitId.ToString();
            
            try
            {
                if (_uncommitted.Any())
                {
                    // Do a quick check if any event in the current stream has the same commit id indicating the effects of this command have already been recorded
                    // Note: if the stream has snapshots we wont be checking ALL previous events - but this is a good spot check
                    var oldCommits = this._committed.Select(x =>
                    {
                        String temp;
                        if (!x.Descriptor.Headers.TryGetValue(CommitHeader, out temp))
                            return Guid.Empty;
                        return Guid.Parse(temp);
                    });
                    if (oldCommits.Any(x => x == commitId))
                        throw new DuplicateCommitException($"Probable duplicate message handled - discarding commit id {commitId}");

                    Logger.Write(LogLevel.Debug, () => $"Event stream [{this.StreamId}] committing {_uncommitted.Count} events");
                    await _store.WriteEvents<T>(this.Bucket, this.StreamId, this._streamVersion, _uncommitted, commitHeaders).ConfigureAwait(false);
                    this._uncommitted.Clear();
                }
                if (_pendingShots.Any())
                {
                    Logger.Write(LogLevel.Debug, () => $"Event stream [{this.StreamId}] committing {_pendingShots.Count} snapshots");
                    await _snapshots.WriteSnapshots<T>(this.Bucket, this.StreamId, _pendingShots, commitHeaders).ConfigureAwait(false);
                    this._pendingShots.Clear();
                }
                if (_outofband.Any())
                {
                    if (_oobHandler == null)
                        Logger.Write(LogLevel.Warn, () => $"OOB events were used on stream [{this.StreamId}] but no publishers have been defined!");
                    else
                    {
                        Logger.Write(LogLevel.Debug, () => $"Event stream [{this.StreamId}] publishing {_outofband.Count} out of band events to {_oobHandler.GetType().Name}");
                        await _oobHandler.Publish<T>(this.Bucket, this.StreamId, _outofband, commitHeaders).ConfigureAwait(false);

                    }
                    this._outofband.Clear();
                }
            }
            catch (WrongExpectedVersionException e)
            {
                throw new VersionException($"Expected version {_streamVersion}", e);
            }
            catch (CannotEstablishConnectionException e)
            {
                throw new PersistenceException(e.Message, e);
            }
            catch (OperationTimedOutException e)
            {
                throw new PersistenceException(e.Message, e);
            }
            catch (EventStoreConnectionException e)
            {
                throw new PersistenceException(e.Message, e);
            }
        }


    }
}