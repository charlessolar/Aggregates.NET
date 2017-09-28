
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

        public Id Id => (this as IState).Id;
        public string Bucket => (this as IState).Bucket;
        public Id[] Parents => (this as IState).Parents;
        public long Version => (this as IState).Version;
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
