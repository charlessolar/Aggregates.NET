using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IEntity
    {
        Id Id { get; }
        string Bucket { get; }

        long Version { get; }
        long StateVersion { get; }
        bool Dirty { get; }
        IFullEvent[] Uncommitted { get; }
    }

    public interface IEntity<TState> : IEntity where TState : IState, new()
    {
        void Instantiate(TState state);
        void Snapshotting();
        TState State { get; }

        void Apply(IEvent @event);
    }
    public interface IChildEntity : IEntity
    {
        IEntity Parent { get; }
    }
    public interface IChildEntity<out TParent> : IChildEntity where TParent : IEntity
    {
        new TParent Parent { get; }
    }
}
