
using System.Collections.Generic;
using System.Linq;
using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using Aggregates.Logging;
using Aggregates.Exceptions;
using Aggregates.Extensions;

namespace Aggregates
{
    public abstract class State<TThis> : IState where TThis : State<TThis>
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(TThis).Name);

        private IMutateState Mutator => StateMutators.For(typeof(TThis));

        // set is for deserialization
        // todo: with the private contract resolver is this needed?
        public Id Id
        {
            get => (this as IState)?.Id;
            set => (this as IState).Id = value;
        }
        public string Bucket
        {
            get => (this as IState).Bucket;
            set => (this as IState).Bucket = value;
        }
        public IParentDescriptor[] Parents
        {
            get => (this as IState).Parents;
            set => (this as IState).Parents = value;
        }
        public long Version
        {
            get => (this as IState).Version;
            set => (this as IState).Version = value;
        }

        // Don't need / want previous snapshots to be deserialized
        // will lead to infinite Snapshot.Snapshot.Snapshot.Snapshot
        public TThis Snapshot => (this as IState).Snapshot as TThis;

        // Trick so we can set these fields ourselves without a constructor
        Id IState.Id { get; set; }
        string IState.Bucket { get; set; }
        IParentDescriptor[] IState.Parents { get; set; }
        long IState.Version { get; set; }
        IState IState.Snapshot { get; set; }
        IEvent[] IState.Committed => _committed.ToArray();

        private List<IEvent> _committed = new List<IEvent>();

        // Allow user to perform and needed initial tasks with the snapshot info
        protected virtual void SnapshotRestored() { }
        protected virtual void Snapshotting() { }

        protected virtual bool ShouldSnapshot() { return false; }
        


        void IState.SnapshotRestored()
        {
            SnapshotRestored();
        }
        void IState.Snapshotting()
        {
            Snapshotting();
        }

        bool IState.ShouldSnapshot()
        {
            return ShouldSnapshot();
        }
        
        void IState.Conflict(IEvent @event)
        {
            Mutator.Conflict(this, @event);
        }

        void IState.Apply(IEvent @event)
        {
            try
            {
                Mutator.Handle(this, @event);
            }
            catch (NoRouteException)
            {
                Logger.DebugEvent("NoRoute", "{State} has no route for {EventType}", typeof(TThis).FullName, @event.GetType().FullName);
            }
            _committed.Add(@event);

            (this as IState).Version++;
        }
    }
}
