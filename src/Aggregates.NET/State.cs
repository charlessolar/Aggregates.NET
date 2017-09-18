
using System.Collections.Generic;
using System.Linq;
using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;

namespace Aggregates
{
    public abstract class State<TThis> : IState where TThis : State<TThis>
    {
        private IMutateState Mutator => StateMutators.For(typeof(TThis));
        
        public long Version { get; private set; }
        public TThis Snapshot { get; private set; }
        
        protected virtual bool ShouldSnapshot() { return false; }
        protected virtual void RestoreSnapshot(TThis snapshot) { }

        IState IState.Snapshot => Snapshot;

        bool IState.ShouldSnapshot()
        {
            return ShouldSnapshot();
        }

        void IState.RestoreSnapshot(IState snapshot)
        {
            Snapshot = (TThis)snapshot;
            
            Version = Snapshot.Version;

            RestoreSnapshot(Snapshot);
        }
        
        void IState.Conflict(IEvent @event)
        {
            Mutator.Conflict(this, @event);
        }

        void IState.Apply(IEvent @event)
        {
            Mutator.Handle(this, @event);
            Version++;
        }
    }
}
