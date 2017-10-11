
using System.Collections.Generic;
using System.Linq;
using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using Aggregates.Logging;
using Aggregates.Exceptions;

namespace Aggregates
{
    public abstract class State<TThis> : IState where TThis : State<TThis>
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(TThis).Name);

        private IMutateState Mutator => StateMutators.For(typeof(TThis));

        // set is for deserialization
        public Id Id
        {
            get => (this as IState).Id;
            set => (this as IState).Id = value;
        }
        public string Bucket
        {
            get => (this as IState).Bucket;
            set => (this as IState).Bucket = value;
        }
        public Id[] Parents
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
        Id[] IState.Parents { get; set; }
        long IState.Version { get; set; }
        IState IState.Snapshot { get; set; }

        protected virtual bool ShouldSnapshot() { return false; }
        
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
                Logger.Debug($"{typeof(TThis).Name} missing handler for event {@event.GetType().Name}");
            }

            (this as IState).Version++;
        }
    }
}
